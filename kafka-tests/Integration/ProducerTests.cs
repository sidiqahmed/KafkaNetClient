﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KafkaNet;
using KafkaNet.Model;
using KafkaNet.Protocol;
using NUnit.Framework;

namespace kafka_tests.Integration
{
    [TestFixture]
    [Category("Integration")]
    public class ProducerTests
    {
        private BrokerRouter _router;

        [SetUp]
        public void Setup()
        {
            var options = new KafkaOptions(new Uri(ConfigurationManager.AppSettings["IntegrationKafkaServerUrl"]));

            _router = new BrokerRouter(options);
        }

        [Test]
        [TestCase(10)]
        //TODO ignoring these for now as the auto test running take forever with this.  
        //[TestCase(100)]
        //[TestCase(1000)]
        //[TestCase(10000)]
        public void SendAsyncShouldHandleHighVolumeOfMessages(int amount)
        {
            var tasks = new Task<List<ProduceResponse>>[amount];
            var producer = new Producer(_router);

            for (var i = 0; i < amount; i++)
            {
                tasks[i] = producer.SendMessageAsync("LoadTest", new Message[] { new Message { Value = Guid.NewGuid().ToString() } });
            }

            var results = tasks.SelectMany(x => x.Result).ToList();

            Assert.That(results.Count, Is.EqualTo(amount));
            Assert.That(results.Any(x => x.Error != 0), Is.False);
        }

        [Test]
        public void ConsumerShouldConsumeInSameOrderAsProduced()
        {
            var producer = new Producer(_router);

            var offsets = producer.GetTopicOffsetAsync("LoadTest").Result;

            var consumer = new Consumer(new ConsumerOptions("LoadTest", _router),
                offsets.Select(x => new OffsetPosition(x.PartitionId, x.Offsets.Max())).ToArray());
            
            var tasks = new List<Task<List<ProduceResponse>>>();
            for (int i = 0; i < 20; i++)
            {
                tasks.Add(producer.SendMessageAsync("LoadTest", new[] { new Message { Value = i.ToString(), Key = "1" } }));
            }
            Task.WaitAll(tasks.ToArray());

            var results = consumer.Consume().Take(20).ToList();

            for (int i = 0; i < 20; i++)
            {
                Assert.That(results[i].Value == i.ToString());
            }
        }
    }
}