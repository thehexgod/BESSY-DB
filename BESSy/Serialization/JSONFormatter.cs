﻿/*
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
using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters;
using System.Text;
using BESSy.Json;
using BESSy.Json.Linq;
using BESSy.Json.Serialization;

namespace BESSy.Serialization
{
    public class JSONFormatter : IQueryableFormatter
    {
        public JSONFormatter() : this(_defaultSettings)
        {
        }

        public JSONFormatter(JsonSerializerSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException("settings", "Serializer settings can not be null.");

            _serializer = JsonSerializer.Create(settings);
            _converters = settings.Converters.ToList().ToArray();
            
        }

        JsonConverter[] _converters;
        JsonSerializer _serializer;

        public bool Trim { get { return false; } }

        /// <summary>
        /// Passthrough for Json
        /// </summary>
        /// <returns></returns>
        public byte[] Format(byte[] buffer)
        {
            return buffer;
        }

        /// <summary>
        /// Passthrough for Json
        /// </summary>
        /// <returns></returns>
        public Stream Format(Stream inStream)
        {
            return inStream;
        }

        /// <summary>
        /// Passthrough for Json
        /// </summary>
        /// <returns></returns>
        public byte[] Unformat(byte[] buffer)
        {
            return buffer;
        }

        /// <summary>
        /// Passthrough for Json
        /// </summary>
        /// <returns></returns>
        public Stream Unformat(Stream inStream)
        {
            return inStream;
        }

        public byte[] FormatObj<T>(T obj)
        {
#if DEBUG
            if (object.Equals(obj, default(T)))
                throw new ArgumentNullException();
#endif

            return ((MemoryStream)FormatObjStream(obj)).ToArray();
        }

        public Stream FormatObjStream<T>(T obj)
        {
#if DEBUG
            if (object.Equals(obj, default(T)))
                throw new ArgumentNullException();
#endif
            var ms = new MemoryStream();
            var sw = new StreamWriter(ms);
            var jw = new JsonTextWriter(sw);

            _serializer.Serialize(jw, obj);

            jw.Flush();
            sw.Flush();
            
            return ms;
        }

        public bool TryFormatObj<T>(T obj, out Stream outStream)
        {
            outStream = null;

            if (object.Equals(obj, default(T)))
                return false;

            try
            {
                outStream = FormatObjStream<T>(obj);

                return true;
            }
            catch (JsonException) { }
            catch (SystemException) { }
            catch (ApplicationException) { }

            return false;
        }

        public bool TryFormatObj<T>(T obj, out byte[] buffer)
        {
            buffer = new byte[] { };

            if (object.Equals(obj, default(T)))
                return false;

            try
            {
                buffer = ((MemoryStream)FormatObjStream<T>(obj)).ToArray();

                return true;
            }
            catch (JsonException) { }
            catch (SystemException) { }
            catch (ApplicationException) { }

            return false;
        }

        public T UnformatObj<T>(byte[] buffer)
        {
            using (var ms = new MemoryStream(buffer))
            {
                return UnformatObj<T>(ms);
            }
        }

        public T UnformatObj<T>(Stream inStream)
        {
            inStream.Position = 0;

            var sr = new StreamReader(inStream);
            var jr = new JsonTextReader(sr);

            return _serializer.Deserialize<T>(jr);
        }

        public bool TryUnformatObj<T>(byte[] buffer, out T obj)
        {
            obj = default(T);

            if (buffer == null)
                return false;

            try
            {
                obj = UnformatObj<T>(buffer);

                return true;
            }
            catch (JsonException) { }
            catch (SystemException) { }
            catch (ApplicationException) { }

            return false;
        }

        public bool TryUnformatObj<T>(Stream stream, out T obj)
        {
            obj = default(T);

            if (stream == null)
                return false;

            try
            {
                obj = UnformatObj<T>(stream);

                return true;
            }
            catch (JsonException) { }
            catch (SystemException) { }
            catch (ApplicationException) { }

            return false;
        }

        public JsonSerializer Serializer { get { return _serializer; } }

        public JObject Parse(Stream inStream)
        {
            inStream.Position = 0;
            using (var sr = new StreamReader(inStream))
                using (var reader = new JsonTextReader(sr))
                    return JObject.Load(reader);
        }

        static readonly JsonSerializerSettings _defaultSettings = new JsonSerializerSettings()
        {
            NullValueHandling = NullValueHandling.Ignore,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            DefaultValueHandling = DefaultValueHandling.Ignore,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ContractResolver = new DefaultContractResolver() { IgnoreSerializableInterface = true },
            Formatting = Formatting.None,
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            TypeNameHandling = TypeNameHandling.Objects,
            TypeNameAssemblyFormat = FormatterAssemblyStyle.Simple,
        };

        public static JsonSerializerSettings GetDefaultSettings()
        {
            return _defaultSettings;
        }
    }
}