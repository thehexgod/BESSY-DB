﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BESSy.Seeding;
using BESSy.Tests.Mocks;
using NUnit.Framework;
using BESSy.Relational;
using BESSy.Files;

namespace BESSy.Tests.RelationshipEntityTests
{
    [TestFixture]
    public class RelationshipEntityTest : FileTest
    {
        protected override void Cleanup()
        {
            base.Cleanup();
        }

        [Test]
        public void SingleRelationshipSaves()
        {
            _testName = System.Reflection.MethodInfo.GetCurrentMethod().Name.GetHashCode().ToString();

            var seed = new Seed32();
            IList<MockClassE> objs = null;
            var ids = new List<int>();

            using (var fLock = new ManagedFileLock(_testName))
            {
                Cleanup();

                using (var db = new RelationalDatabase<int, MockClassD>(_testName + ".database", "Id"))
                {
                    db.Load();

                    using (var t = db.BeginTransaction())
                    {
                        objs = TestResourceFactory.GetMockClassDObjects(3, db).ToList();

                        objs.ToList().ForEach(o => o.Id = db.Add(o));

                        t.Commit();
                    }
                }

                using (var db = new RelationalDatabase<int, MockClassD>(_testName + ".database"))
                {
                    db.Load();

                    foreach (var obj in objs)
                    {
                        var orig = obj as MockClassE;
                        var item = db.Fetch(obj.Id) as MockClassE;

                        AssertMockClassE(item, orig, db);
                    }
                }
            }
        }

        [Test]
        public void SingleRelationshipSavesAndSelects()
        {
            _testName = System.Reflection.MethodInfo.GetCurrentMethod().Name.GetHashCode().ToString();

            var seed = new Seed32();
            List<MockClassE> objs = null;
            var ids = new List<int>();

            using (var fLock = new ManagedFileLock(_testName))
            {
                Cleanup();

                using (var db = new RelationalDatabase<int, MockClassD>(_testName + ".database", "Id"))
                {
                    db.Load();

                    using (var t = db.BeginTransaction())
                    {
                        objs = TestResourceFactory.GetMockClassDObjects(3, db).ToList();

                        objs.ToList().ForEach(o => o.Id = db.Add(o));

                        t.Commit();
                    }
                }

                using (var db = new RelationalDatabase<int, MockClassD>(_testName + ".database"))
                {
                    db.Load();

                    var first = db.SelectFirst(s => s.Value<int>("Id") > 0, 50);

                    foreach (var obj in objs)
                        AssertMockClassE((MockClassE)first.First(f => f.Id == obj.Id), obj as MockClassE, db);

                    var last = db.SelectLast(s => s.Value<int>("Id") > 0, 50);

                    foreach (var obj in objs)
                        AssertMockClassE((MockClassE)last.First(f => f.Id == obj.Id), obj as MockClassE, db);
                }
            }
        }

        [Test]
        public void SingleRelationshipSavesAndDeletes()
        {
            _testName = System.Reflection.MethodInfo.GetCurrentMethod().Name.GetHashCode().ToString();


            var seed = new Seed32();
            List<MockClassE> objs = null;
            var ids = new List<int>();

            using (var fLock = new ManagedFileLock(_testName))
            {
                Cleanup();

                using (var db = new RelationalDatabase<int, MockClassD>(_testName + ".database", "Id"))
                {
                    db.Load();

                    using (var t = db.BeginTransaction())
                    {
                        objs = TestResourceFactory.GetMockClassDObjects(3, db).ToList();

                        objs.ToList().ForEach(o => o.Id = db.Add(o));

                        t.Commit();
                    }
                }

                using (var db = new RelationalDatabase<int, MockClassD>(_testName + ".database"))
                {
                    db.Load();

                    var first = db.Fetch(objs[0].Id);

                    using (var t = db.BeginTransaction())
                    {
                        first.LowBall = new List<MockClassD>();

                        db.Update(first, first.Id);

                        t.Commit();
                    }

                    first = db.Fetch(objs[0].Id);

                    Assert.AreEqual(0, first.LowBall.Count());
                }

                using (var db = new RelationalDatabase<int, MockClassD>(_testName + ".database"))
                {
                    db.Load();

                    var first = db.Fetch(objs.First().Id);

                    Assert.AreEqual(0, first.LowBall.Count());
                }
            }
        }

        internal static void AssertMockClassE(MockClassE item, MockClassE orig, IRelationalDatabase<int, MockClassD> repo)
        {
            if (item == null && orig == null)
                return;

            Assert.IsNotNull(item);
            Assert.IsNotNull(orig);

            orig.SetRepository(repo);

            Assert.AreEqual(item.Id, orig.Id);
            Assert.AreEqual(item.Name, orig.Name);
            Assert.AreEqual(item.GetSomeCheckSum[0], orig.GetSomeCheckSum[0]);
            Assert.AreEqual(item.NamesOfStuff[0], orig.NamesOfStuff[0]);
            Assert.AreEqual(item.Location.X, orig.Location.X);
            Assert.AreEqual(item.Location.Y, orig.Location.Y);
            Assert.AreEqual(item.Location.Z, orig.Location.Z);
            Assert.AreEqual(item.Location.W, orig.Location.W);
            Assert.AreEqual(item.ReferenceCode, orig.ReferenceCode);
            Assert.AreEqual(item.ReplicationID, orig.ReplicationID);
            Assert.AreEqual(item.CatalogName, orig.CatalogName);
            Assert.AreEqual(item.CatalogNameNull, orig.CatalogNameNull);

            AssertMockClassE(item.HiC as MockClassE, orig.HiC as MockClassE, repo);
            AssertMockClassE(item.Parent, orig.Parent, repo);

            for (var i = 0; i < item.LowBall.Count(); i++)
                AssertMockClassE(item.LowBall.ToArray()[i] as MockClassE, orig.LowBall.ToArray()[i] as MockClassE, repo);
        }
    }
}

