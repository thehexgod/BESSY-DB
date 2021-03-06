﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using System.Reflection;
using BESSy.Tests.Mocks;
using BESSy.Relational;
using BESSy.Serialization;
using BESSy.Seeding;
using BESSy.Serialization.Converters;
using BESSy.Json.Linq;
using BESSy.Transactions;
using BESSy.Factories;

namespace BESSy.Tests.ProxyTests
{
    [TestFixture]
	public class MockProxyTests : FileTest
	{
        [Test]
        public void ProxySavesData()
        {
            _testName = MethodInfo.GetCurrentMethod().Name.GetHashCode().ToString();
            Cleanup();

            var domain = TestResourceFactory.CreateRandomDomain();

            using (var db = new PocoRelationalDatabase<int, MockClassA>
                (_testName + ".database", "Id",
                new FileCore<int, long>(), new BinConverter32(), new BSONFormatter(),
                new TransactionManager<int, JObject>(), new AtomicFileManagerFactory(), new DatabaseCacheFactory(),
                new IndexFileFactory(), new IndexFactory(), new MockProxyFactory<int, MockClassA>()))
            {
                db.Load();

                using (var t = db.BeginTransaction())
                {
                    domain.Id = db.Add(domain);

                    var d = db.Fetch(domain.Id);

                    Validation.ValidateDomain(d as MockDomain, domain as MockDomain);

                    t.Commit();
                }
            }

            using (var db = new PocoRelationalDatabase<int, MockClassA>(_testName + ".database",
                new BSONFormatter(), 
                new TransactionManager<int, JObject>(), 
                new AtomicFileManagerFactory(), 
                new DatabaseCacheFactory(), 
                new IndexFileFactory(), 
                new IndexFactory(), 
                new MockProxyFactory<int, MockClassA>()))
            {
                db.Load();

                var d = db.Fetch(domain.Id);

                Validation.ValidateDomain(d as MockDomain, domain as MockDomain);
            }
        }
	}
}
