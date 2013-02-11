﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
/*
Copyright (c) 2011,2012,2013 Kristen Mallory dba Klink

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"),
to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, 
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, 
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

*/
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Security.AccessControl;
using BESSy.Seeding;
using BESSy.Serialization.Converters;
using BESSy.Files;
using BESSy.Extensions;
using BESSy.Serialization;
using BESSy.Cache;

namespace BESSy
{
    public interface IIndexRepository<IdType, PropertyType> : IMappedRepository<IndexPropertyPair<IdType, PropertyType>, IdType>
    {
        ISeed<IdType> Seed { get; }
        IList<IdType> RidLookup(PropertyType property);
        void FlushSeed();
    }

    public class IndexRepository<IdType, PropertyType> : IIndexRepository<IdType, PropertyType>
    {
        static ISafeFormatter DefaultFormatter { get { return new BSONFormatter(); } }
        static IBatchFileManager<IndexPropertyPair<IdType, PropertyType>> DefaultFileManager
        { get { return new BatchFileManager<IndexPropertyPair<IdType, PropertyType>>(DefaultFormatter); } }

        /// <summary>
        /// Opens an existing index with the specified file name.
        /// </summary>
        /// <param name="fileName"></param>
        public IndexRepository(string fileName)
            : this(fileName, -1, DefaultFormatter, DefaultFileManager)
        {

        }

        /// <summary>
        /// Opens an existing index with the specified settings.
        /// </summary>
        /// <param name="cacheSize"></param>
        /// <param name="fileName"></param>
        /// <param name="mapFormatter"></param>
        /// <param name="fileManager"></param>
        public IndexRepository(string fileName, int cacheSize, ISafeFormatter mapFormatter, IBatchFileManager<IndexPropertyPair<IdType, PropertyType>> fileManager)
        {
            _create = false;
            AutoCache = true;
            CacheSize = cacheSize;

            _fileName = fileName;
            _mapFormatter = mapFormatter;
            _fileManager = fileManager;
        }

        /// <summary>
        /// Creates or opens an existing index with the specified filename.
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="propertyConverter"></param>
        public IndexRepository
            (string fileName
            ,IBinConverter<PropertyType> propertyConverter) 
            : this(-1, fileName
            , TypeFactory.GetSeedFor<IdType>()
            , TypeFactory.GetBinConverterFor<IdType>()
            , propertyConverter
            , DefaultFormatter
            , DefaultFileManager)
        {
        }

        /// <summary>
        /// Creates or opens an existing index with the specified filename.
        /// </summary>
        /// <param name="cacheSize"></param>
        /// <param name="fileName"></param>
        /// <param name="seed"></param>
        /// <param name="idConverter"></param>
        /// <param name="propertyConverter"></param>
        /// <param name="mapFormatter"></param>
        /// <param name="fileManager"></param>
        public IndexRepository
            (int cacheSize,
            string fileName, 
            ISeed<IdType> seed,
            IBinConverter<IdType> idConverter,
            IBinConverter<PropertyType> propertyConverter,
            ISafeFormatter mapFormatter,
            IBatchFileManager<IndexPropertyPair<IdType, PropertyType>> fileManager)
        {
            _create = true;
            AutoCache = true;
            _fileName = fileName;
            _fileManager = fileManager;
            _mapFormatter = mapFormatter;

            seed.IdConverter = idConverter;
            seed.PropertyConverter = propertyConverter;
            seed.Stride = idConverter.Length + propertyConverter.Length;
            Seed = seed;
        }
        
        List<IdType> _cacheQueue = new List<IdType>();
        IDictionary<IdType, PropertyType> _cache = new SortedDictionary<IdType, PropertyType>();
        List<IDictionary<IdType, PropertyType>> _stagingCache = new List<IDictionary<IdType, PropertyType>>();
        Queue<long> _fileFlushQueue = new Queue<long>();

        bool _inFlush;
        bool _create;
        string _fileName;
        ISafeFormatter _mapFormatter;
        IBatchFileManager<IndexPropertyPair<IdType, PropertyType>> _fileManager;
        IIndexMapManager<IdType, PropertyType> _mapFileManager;
        IBinConverter<IdType> _idConverter;

        protected bool _cacheIsDirty { get; set; }
        protected object _syncCache = new object();
        protected object _syncStaging = new object();
        protected object _syncFileQueue = new object();
        protected object _syncFileFlush = new object();

        protected virtual void HandleOnFlushCompleted(IDictionary<IdType, PropertyType> itemsFlushed)
        {
            Trace.TraceInformation("Flush Completed Handler Called.");

            try
            {
                if (itemsFlushed != null)
                    lock (_syncStaging)
                        itemsFlushed.Clear();

                int fileQueue = 0;
                lock (_syncFileQueue)
                    fileQueue = _fileFlushQueue.Count;

                if (fileQueue > 0 && !_mapFileManager.FlushQueueActive)
                    FlushToFile();
            }
            catch (Exception ex)
            { Trace.TraceError(ex.ToString()); throw; }
        }

        protected virtual void UpdateCache(IdType id, PropertyType property, bool isDirty, bool forceCache = false)
        {
            _cacheIsDirty |= isDirty;

            if (_idConverter.Compare(id, default(IdType)) == 0)
                return;

            if (_cache.ContainsKey(id))
                _cache[id] = property;

            else if (_cacheQueue.Contains(id))
                _cache.Add(id, property);

            else if (AutoCache || forceCache)
                _cache.Add(id, property);

            if (AutoCache && _cache.Count > CacheSize)
                Sweep();
        }

        public virtual int CacheSize { get; set; }

        public virtual bool AutoCache { get; set; }

        public virtual ISeed<IdType> Seed { get; protected set; }

        public bool FileFlushQueueActive
        {
            get
            {
                if (_inFlush)
                    return true;

                int count = 0;

                lock (_syncFileQueue)
                    count = _fileFlushQueue.Count;

                return count > 0 || _mapFileManager.FlushQueueActive;
            }
        }

        public virtual int Load()
        {
            lock (_syncFileFlush)
            {

                lock (_syncCache)
                {
                    _cache.Clear();

                    _cacheQueue.Clear();

                    _cacheIsDirty = false;

                    lock (_syncStaging)
                        _stagingCache.Clear();
                }

                var fi = new FileInfo(_fileName);

                if (!fi.Directory.Exists)
                    CreateDirectory(fi);

                if (!fi.Exists)
                {
                    if (_create)
                        using (var c = _fileManager.GetWritableFileStream(_fileName))
                            c.Flush();
                    else
                        throw new FileNotFoundException("File not found.", _fileName);
                }

                using (var stream = _fileManager.GetReadableFileStream(_fileName))
                {
                    var count = _fileManager.GetBatchedSegmentCount(stream);

                    stream.Position = 0;

                    var seed = _fileManager.LoadSeedFrom<IdType>(stream);

                    if (count > 0 && !object.Equals(seed, default(ISeed<IdType>)))
                        InitializeDatabase(seed, count);
                    else
                        InitializeDatabase(Seed, count);

                    var batch = LoadBatchFromFile(stream);

                    var seg = 0;

                    while (seg < count && batch.IsNotNullAndNotEmpty())
                    {
                        seg = _mapFileManager.SaveBatchToFile(batch, seg);

                        batch = LoadBatchFromFile(stream);
                    }
                }
            }

            return Count();
        }

        protected virtual void InitializeDatabase(ISeed<IdType> seed, int count)
        {
            if (CacheSize < 1)
                CacheSize = Caching.DetermineOptimumCacheSize(seed.Stride);

            Seed = seed;

            _idConverter = (IBinConverter<IdType>)seed.IdConverter;

            if (CacheSize < 1)
                CacheSize = Caching.DetermineOptimumCacheSize(seed.Stride);

            _mapFileManager = new IndexMapManager<IdType, PropertyType>
                (_fileName
                , (IBinConverter<IdType>)seed.IdConverter
                , (IBinConverter<PropertyType>)seed.PropertyConverter);

            _mapFileManager.OpenOrCreate(_fileName, count, seed.Stride);
            _mapFileManager.OnFlushCompleted +=new FlushCompleted<PropertyType,IdType>(HandleOnFlushCompleted);
        }

        protected virtual IList<IndexPropertyPair<IdType, PropertyType>> LoadBatchFromFile(Stream stream)
        {
            return _fileManager.LoadBatchFrom(stream);
        }

        protected virtual void CreateDirectory(FileInfo fi)
        {
            fi.Directory.Create();

            DirectorySecurity dirSec = Directory.GetAccessControl(fi.Directory.FullName);
            dirSec.AddAccessRule(new FileSystemAccessRule("Everyone", FileSystemRights.Write, AccessControlType.Allow));
            dirSec.AddAccessRule(new FileSystemAccessRule("Everyone", FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            dirSec.AddAccessRule(new FileSystemAccessRule("Everyone", FileSystemRights.CreateFiles, AccessControlType.Allow));
            Directory.SetAccessControl(fi.Directory.FullName, dirSec);

            GC.Collect();

            GC.WaitForFullGCComplete();
        }

        protected virtual void FlushCache()
        {
            lock (_syncCache)
            {
                if (_stagingCache.Count > 0)
                    lock (_syncStaging)
                        _stagingCache.RemoveAll(s => s.Count == 0);

                IDictionary<IdType, PropertyType> sort = new SortedDictionary<IdType, PropertyType>(_cache);

                lock (_syncStaging)
                    _stagingCache.Add(sort);

                _mapFileManager.Flush(sort);

                ClearCache();

                _cacheIsDirty = false;
            }
        }

        public virtual void Flush()
        {
            if (_cacheIsDirty)
            {
                lock (_syncFileQueue)
                    _fileFlushQueue.Enqueue(DateTime.Now.Ticks);

                FlushCache();
            }
            else if (!_mapFileManager.FlushQueueActive)
                FlushToFile();
        }

        protected virtual void FlushToFile()
        {

            if (!Monitor.TryEnter(_syncFileFlush, 500))
                return;

            try
            {
                Trace.TraceInformation("_syncFileFlush entered.");

                _inFlush = true;

                long queue = 0;
                lock (_syncFileQueue)
                    if (_fileFlushQueue.Count > 0)
                        queue = _fileFlushQueue.Dequeue();

                while (queue > 0)
                {
                    var tempFileName = _fileName + Guid.NewGuid().ToString();

                    using (var f = _fileManager.GetWritableFileStream(tempFileName))
                    {
                        long position = _fileManager.SaveSeed<IdType>(f, Seed);

                        List<IndexPropertyPair<IdType, PropertyType>> batch = new List<IndexPropertyPair<IdType, PropertyType>>();

                        foreach (var item in _mapFileManager)
                        {
                            if (object.Equals(default(IndexPropertyPair<IdType, PropertyType>), item))
                                continue;

                            batch.Add(item);

                            if (batch.Count > CacheSize)
                            {
                                position = _fileManager.SaveBatch(f, batch, position);

                                batch.Clear();
                            }
                        }

                        if (batch.Count > 0)
                            position = _fileManager.SaveBatch(f, batch, position);

                        f.SetLength(f.Position);

                        f.Flush();
                        f.Close();

                        GC.Collect();

                        GC.WaitForFullGCComplete(5000);

                        FileInfo fi = new FileInfo(tempFileName);

                        fi.Replace(_fileName, _fileName + ".old", true);
                    }

                    lock (_syncFileQueue)
                        if (_fileFlushQueue.Count > 0)
                            queue = _fileFlushQueue.Dequeue();
                        else queue = 0;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
                throw;
            }
            finally
            {
                Monitor.Exit(_syncFileFlush);

                Trace.TraceInformation("_syncFileFlush exited.");

                _inFlush = false;
            }
        }

        public virtual void FlushSeed()
        {
            lock (_syncFileFlush)
            {
                Trace.TraceInformation("_syncFileFlush entered.");

                _inFlush = true;

                var tempFileName = _fileName + Guid.NewGuid().ToString();

                using (var f = _fileManager.GetWritableFileStream(tempFileName))
                {
                    long position = _fileManager.SaveSeed<IdType>(f, Seed);

                    f.Flush();
                    f.Close();

                    GC.Collect();

                    GC.WaitForFullGCComplete(5000);

                    FileInfo fi = new FileInfo(tempFileName);

                    fi.Replace(_fileName, _fileName + ".old", true);
                }

                Trace.TraceInformation("_syncFileFlush exited.");

                _inFlush = false;
            }
        }

        public virtual IdType Add(IndexPropertyPair<IdType, PropertyType> item)
        {
            if (_idConverter.Compare(item.Id, default(IdType)) == 0)
                return default(IdType);

            UpdateCache(item.Id, item.Property, true, true);

            return item.Id;
        }

        public void AddOrUpdate(IndexPropertyPair<IdType, PropertyType> item, IdType id)
        {
            if (object.Equals(item, default(IndexPropertyPair<IdType, PropertyType>)))
                throw new ArgumentException("Indexer was null.", "item");

            if (_idConverter.Compare(item.Id, id) != 0)
                Delete(id);

            UpdateCache(item.Id, item.Property, true, true);
        }

        public void Update(IndexPropertyPair<IdType, PropertyType> item, IdType id)
        {
            if (_idConverter.Compare(id, default(IdType)) == 0)
                id = item.Id;

            if (_idConverter.Compare(item.Id, id) != 0)
                Delete(id);

            UpdateCache(item.Id, item.Property, true, true);
        }

        public IndexPropertyPair<IdType, PropertyType> Fetch(IdType id)
        {
            IndexPropertyPair<IdType, PropertyType> item; // = default(IndexPropertyPair<IdType, PropertyType>);

            if (Contains(id))
                item = GetFromCache(id);
            else
                item = new IndexPropertyPair<IdType, PropertyType>(id, _mapFileManager.Load(id));

            UpdateCache(id, item.Property, false);

            return item;
        }

        public IList<IdType> RidLookup(PropertyType property)
        {
            var ids = _mapFileManager.RidLookup(property);

            return ids;
        }

        public virtual void Delete(IdType id)
        {
            if (_mapFileManager.Save(default(PropertyType), id))
                _mapFileManager.Detach(id);

            UpdateCache(id, default(PropertyType), true, true);
        }

        #region ILinqRepository<PropertyType,IdType> Members

        public IEnumerable<IndexPropertyPair<IdType, PropertyType>> Take(int count)
        {
            lock (_syncFileFlush)
                return Enumerable.Take(_mapFileManager, count);
        }

        public IEnumerable<IndexPropertyPair<IdType, PropertyType>> Skip(int count)
        {
            if (count > this._mapFileManager.Length)
                throw new ArgumentException("items to skip is greater than the size of the repository.");

            lock (_syncFileFlush)
                return Enumerable.Skip(_mapFileManager, count);
        }

        public IEnumerable<IndexPropertyPair<IdType, PropertyType>> Where(Func<IndexPropertyPair<IdType, PropertyType>, bool> query)
        {
            lock (_syncFileFlush)
                return Enumerable.Where(_mapFileManager, query);
        }

        public IndexPropertyPair<IdType, PropertyType> First(Func<IndexPropertyPair<IdType, PropertyType>, bool> query)
        {
            lock (_syncFileFlush)
                return Enumerable.First(_mapFileManager, query);
        }

        public IndexPropertyPair<IdType, PropertyType> FirstOrDefault(Func<IndexPropertyPair<IdType, PropertyType>, bool> query)
        {
            lock (_syncFileFlush)
                return Enumerable.FirstOrDefault(_mapFileManager, query);
        }

        public IndexPropertyPair<IdType, PropertyType> Last(Func<IndexPropertyPair<IdType, PropertyType>, bool> query)
        {
            lock (_syncFileFlush)
                return Enumerable.Last(_mapFileManager, query);
        }

        public IndexPropertyPair<IdType, PropertyType> LastOrDefault(Func<IndexPropertyPair<IdType, PropertyType>, bool> query)
        {
            lock (_syncFileFlush)
                return Enumerable.LastOrDefault(_mapFileManager, query);
        }

        public IEnumerable<TResult> OfType<TResult>()
        {
            lock (_syncFileFlush)
                return Enumerable.OfType<IndexPropertyPair<IdType, TResult>>(_mapFileManager).Select(m => m.Property);
        }

        public IQueryable<IndexPropertyPair<IdType, PropertyType>> AsQueryable()
        {
            lock (_syncFileFlush)
                return _mapFileManager.AsQueryable<IndexPropertyPair<IdType, PropertyType>>();
        }

        #endregion

        public virtual void Sweep()
        {
            lock (_syncStaging)
                if (_stagingCache.Count > 0)
                    _stagingCache.RemoveAll(s => s == null || s.Count == 0);

            lock (_syncCache)
            {
                if (_cache.Count <= CacheSize)
                    return;

                if (_cacheIsDirty)
                {
                    FlushCache();
                    return;
                }

                var diff = (_cache.Count - CacheSize);

                if (diff <= 0)
                    return;

                var toRemove = _cacheQueue.Distinct().Take(CacheSize / 2).ToList();

                foreach (var id in toRemove)
                {
                    _cacheQueue.RemoveAll(c => _idConverter.Compare(c, id) == 0);
                    _cache.Remove(id);
                }
            }
        }

        public int Count()
        {
            if (_mapFileManager != null)
                return _mapFileManager.Length;

            return 0;
        }

        public void Clear()
        {
            if (_stagingCache != null)
                lock (_syncStaging)
                    _stagingCache.Clear();

            if (_mapFileManager != null)
                _mapFileManager.ClearCache();

            if (_fileFlushQueue != null)
                lock (_syncFileFlush)
                    _fileFlushQueue.Clear();

            ClearCache();
        }

        public bool IsNew(IdType id)
        {
            return false;
        }

        public bool Contains(IdType id)
        {
            lock (_syncCache)
                lock (_syncStaging)
                    return _cache.ContainsKey(id)
                        || _stagingCache.Any(c => c.ContainsKey(id));
        }

        public IndexPropertyPair<IdType, PropertyType> GetFromCache(IdType id)
        {
            lock (_syncCache)
            {
                lock (_syncStaging)
                {
                    if (_cache.ContainsKey(id))
                        return new IndexPropertyPair<IdType, PropertyType>(id, _cache[id]);
                    else if (_stagingCache.Any(c => c.ContainsKey(id)))
                        return new IndexPropertyPair<IdType, PropertyType>
                            (id, _stagingCache.Last(s => s.ContainsKey(id))[id]);
                }
            }

            return default(IndexPropertyPair<IdType, PropertyType>);
        }

        public void CacheItem(IdType id)
        {
            lock (_syncCache)
                _cacheQueue.Add(id);
        }

        public void Detach(IdType id)
        {
            lock (_syncCache)
            {
                if (_cacheQueue.Contains(id))
                    _cacheQueue.Remove(id);

                if (_cache.ContainsKey(id))
                    _cache.Remove(id);
            }
        }

        public void ClearCache()
        {
            lock (_syncCache)
            {
                if (_cacheQueue != null)
                    _cacheQueue.Clear();

                if (_cache != null)
                    _cache.Clear();

                _cacheIsDirty = false;
            }
        }

        public void Dispose()
        {
            if (_cacheIsDirty)
                Flush();

            while (FileFlushQueueActive)
                Thread.Sleep(100);

            Clear();

            if (_fileManager != null)
                _fileManager.Dispose();

            if (_mapFileManager != null)
            {
                _mapFileManager.OnFlushCompleted -= new FlushCompleted<PropertyType, IdType>(HandleOnFlushCompleted);

                _mapFileManager.Dispose();
            }
        }
    }
}
