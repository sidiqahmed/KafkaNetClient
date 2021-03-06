﻿using kafka_tests.Helpers;
using KafkaNet;
using KafkaNet.Model;
using KafkaNet.Protocol;
using Moq;
using Ninject.MockingKernel.Moq;
using NUnit.Framework;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace kafka_tests.Unit
{
    [TestFixture]
    [Category("unit")]
    public class ProtocolGatewayTest
    {
        private MoqMockingKernel _kernel;
        private Mock<IKafkaConnection> _mockKafkaConnection1;
        private Mock<IKafkaConnectionFactory> _mockKafkaConnectionFactory;
        private int _partitionId = 0;

        [SetUp]
        public void Setup()
        {
            _kernel = new MoqMockingKernel();
            _mockKafkaConnection1 = _kernel.GetMock<IKafkaConnection>();
            _mockKafkaConnectionFactory = _kernel.GetMock<IKafkaConnectionFactory>();
            _mockKafkaConnectionFactory.Setup(x => x.Create(It.Is<KafkaEndpoint>(e => e.Endpoint.Port == 1), It.IsAny<TimeSpan>(),
                        It.IsAny<IKafkaLog>(), It.IsAny<int>(), It.IsAny<TimeSpan?>(), It.IsAny<StatisticsTrackerOptions>())).Returns(() => _mockKafkaConnection1.Object);
            _mockKafkaConnectionFactory.Setup(x => x.Resolve(It.IsAny<Uri>(), It.IsAny<IKafkaLog>()))
                .Returns<Uri, IKafkaLog>((uri, log) => new KafkaEndpoint
                {
                    Endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), uri.Port),
                    ServeUri = uri
                });
        }

        [TestCase(ErrorResponseCode.NotLeaderForPartition)]
        [TestCase(ErrorResponseCode.LeaderNotAvailable)]
        [TestCase(ErrorResponseCode.ConsumerCoordinatorNotAvailableCode)]
        [TestCase(ErrorResponseCode.BrokerNotAvailable)]
        [Test, Repeat(IntegrationConfig.NumberOfRepeat)]
        public async Task ShouldTryToRefreshMataDataIfCanRecoverByRefreshMetadata(ErrorResponseCode code)
        {
            var routerProxy = new BrokerRouterProxy(_kernel);
            routerProxy._cacheExpiration = new TimeSpan(10);
            var router = routerProxy.Create();
            ProtocolGateway protocolGateway = new ProtocolGateway(router);

            routerProxy.BrokerConn0.FetchResponseFunction = FailedInFirstMessageError(code, routerProxy._cacheExpiration);
            routerProxy.BrokerConn0.MetadataResponseFunction = BrokerRouterProxy.CreateMetadataResponseWithMultipleBrokers;

            await protocolGateway.SendProtocolRequest(new FetchRequest(), BrokerRouterProxy.TestTopic, _partitionId);

            Assert.That(routerProxy.BrokerConn0.MetadataRequestCallCount, Is.EqualTo(2));
            Assert.That(routerProxy.BrokerConn0.FetchRequestCallCount, Is.EqualTo(2));
        }
        
        [ExpectedException(typeof(FormatException))]
        [Test, Repeat(IntegrationConfig.NumberOfRepeat)]
        public async Task ShouldThrowFormatExceptionWhenTopicIsInvalid()
        {
            var routerProxy = new BrokerRouterProxy(_kernel);
            var router = routerProxy.Create();
            string invalidTopic = " ";
            var fetchRequest = new FetchRequest();
            ProtocolGateway protocolGateway = new ProtocolGateway(router);
            await protocolGateway.SendProtocolRequest(fetchRequest, invalidTopic, 0);
        }

        [Test, Repeat(IntegrationConfig.NumberOfRepeat)]
        [TestCase(typeof(BrokerConnectionException))]
        [TestCase(typeof(ResponseTimeoutException))]
        [TestCase(typeof(NoLeaderElectedForPartition))]
        [TestCase(typeof(LeaderNotFoundException))]
        public async Task ShouldTryToRefreshMataDataIfOnExceptions(Type exceptionType)
        {
            var routerProxy = new BrokerRouterProxy(_kernel);
            routerProxy._cacheExpiration = TimeSpan.FromMilliseconds(10);
            var router = routerProxy.Create();
            ProtocolGateway protocolGateway = new ProtocolGateway(router);

            routerProxy.BrokerConn0.FetchResponseFunction = FailedInFirstMessageException(exceptionType, routerProxy._cacheExpiration);
            routerProxy.BrokerConn0.MetadataResponseFunction = BrokerRouterProxy.CreateMetadataResponseWithMultipleBrokers;

            await protocolGateway.SendProtocolRequest(new FetchRequest(), BrokerRouterProxy.TestTopic, _partitionId);

            Assert.That(routerProxy.BrokerConn0.MetadataRequestCallCount, Is.EqualTo(2));
            Assert.That(routerProxy.BrokerConn0.FetchRequestCallCount, Is.EqualTo(2));
        }

        [TestCase(typeof(Exception))]
        [TestCase(typeof(KafkaApplicationException))]
        public async Task SendProtocolRequestShouldThrowException(Type exceptionType)
        {
            var routerProxy = new BrokerRouterProxy(_kernel);
            routerProxy._cacheExpiration = TimeSpan.FromMilliseconds(10);
            var router = routerProxy.Create();
            ProtocolGateway protocolGateway = new ProtocolGateway(router);

            routerProxy.BrokerConn0.FetchResponseFunction = FailedInFirstMessageException(exceptionType, routerProxy._cacheExpiration);
            routerProxy.BrokerConn0.MetadataResponseFunction = BrokerRouterProxy.CreateMetadataResponseWithMultipleBrokers;
            try
            {
                await protocolGateway.SendProtocolRequest(new FetchRequest(), BrokerRouterProxy.TestTopic, _partitionId);
                Assert.IsTrue(false, "Should throw exception");
            }
            catch (Exception ex)
            {
                Assert.That(ex.GetType(), Is.EqualTo(exceptionType));
            }
        }

        [Test, Repeat(IntegrationConfig.NumberOfRepeat)]
        [ExpectedException(typeof(KafkaApplicationException))]
        [TestCase(ErrorResponseCode.InvalidMessage)]
        [TestCase(ErrorResponseCode.InvalidMessageSize)]
        [TestCase(ErrorResponseCode.MessageSizeTooLarge)]
        [TestCase(ErrorResponseCode.OffsetMetadataTooLargeCode)]
        [TestCase(ErrorResponseCode.OffsetOutOfRange)]
        [TestCase(ErrorResponseCode.NotCoordinatorForConsumerCode)]
        [TestCase(ErrorResponseCode.RequestTimedOut)]
        [TestCase(ErrorResponseCode.OffsetsLoadInProgressCode)]
        [TestCase(ErrorResponseCode.UnknownTopicOrPartition)]
        [TestCase(ErrorResponseCode.Unknown)]
        [TestCase(ErrorResponseCode.StaleControllerEpochCode)]
        [TestCase(ErrorResponseCode.ReplicaNotAvailable)]
        public async Task SendProtocolRequestShouldNoTryToRefreshMataDataIfCanNotRecoverByRefreshMetadata(
            ErrorResponseCode code)
        {
            var routerProxy = new BrokerRouterProxy(_kernel);
            routerProxy._cacheExpiration = TimeSpan.FromMilliseconds(10);
            var router = routerProxy.Create();
            ProtocolGateway protocolGateway = new ProtocolGateway(router);

            routerProxy.BrokerConn0.FetchResponseFunction = FailedInFirstMessageError(code, routerProxy._cacheExpiration);
            routerProxy.BrokerConn0.MetadataResponseFunction = BrokerRouterProxy.CreateMetadataResponseWithMultipleBrokers;
            await protocolGateway.SendProtocolRequest(new FetchRequest(), BrokerRouterProxy.TestTopic, _partitionId);
        }

        [Test, Repeat(IntegrationConfig.NumberOfRepeat)]
        public async Task ShouldUpdateMetadataOnes()
        {
            var routerProxy = new BrokerRouterProxy(_kernel);
            routerProxy._cacheExpiration = TimeSpan.FromMilliseconds(10);
            var router = routerProxy.Create();
            ProtocolGateway protocolGateway = new ProtocolGateway(router);

            routerProxy.BrokerConn0.FetchResponseFunction = ShouldReturnValidMessage;
            routerProxy.BrokerConn0.MetadataResponseFunction = BrokerRouterProxy.CreateMetadataResponseWithMultipleBrokers;
            int numberOfCall = 1000;
            Task[] tasks = new Task[numberOfCall];
            for (int i = 0; i < numberOfCall / 2; i++)
            {
                tasks[i] = protocolGateway.SendProtocolRequest(new FetchRequest(), BrokerRouterProxy.TestTopic, _partitionId);
            }
            await Task.Delay(routerProxy._cacheExpiration);
            await Task.Delay(1);
            for (int i = 0; i < numberOfCall / 2; i++)
            {
                tasks[i + numberOfCall / 2] = protocolGateway.SendProtocolRequest(new FetchRequest(), BrokerRouterProxy.TestTopic, _partitionId);
            }

            await Task.WhenAll(tasks);
            Assert.That(routerProxy.BrokerConn0.FetchRequestCallCount, Is.EqualTo(numberOfCall));
            Assert.That(routerProxy.BrokerConn0.MetadataRequestCallCount, Is.EqualTo(1));
        }

        [Test, Repeat(IntegrationConfig.NumberOfRepeat)]
        public async Task ShouldRecoverUpdateMetadataForNewTopic()
        {
            var routerProxy = new BrokerRouterProxy(_kernel);
            routerProxy._cacheExpiration = TimeSpan.FromMilliseconds(10);
            var router = routerProxy.Create();

            ProtocolGateway protocolGateway = new ProtocolGateway(router);
            var fetchRequest = new FetchRequest();

            routerProxy.BrokerConn0.FetchResponseFunction = ShouldReturnValidMessage;
            routerProxy.BrokerConn0.MetadataResponseFunction = BrokerRouterProxy.CreateMetadataResponseWithMultipleBrokers;
            int numberOfCall = 1000;
            Task[] tasks = new Task[numberOfCall];
            for (int i = 0; i < numberOfCall / 2; i++)
            {
                tasks[i] = protocolGateway.SendProtocolRequest(fetchRequest, BrokerRouterProxy.TestTopic, _partitionId);
            }

            routerProxy.BrokerConn0.MetadataResponseFunction = async () =>
            {
                var response = await BrokerRouterProxy.CreateMetadataResponseWithMultipleBrokers();
                response.Topics[0].Name = "test2";
                return response;
            };

            for (int i = 0; i < numberOfCall / 2; i++)
            {
                tasks[i + numberOfCall / 2] = protocolGateway.SendProtocolRequest(fetchRequest, "test2", _partitionId);
            }

            await Task.WhenAll(tasks);
            Assert.That(routerProxy.BrokerConn0.FetchRequestCallCount, Is.EqualTo(numberOfCall));
            Assert.That(routerProxy.BrokerConn0.MetadataRequestCallCount, Is.EqualTo(2));
        }

        [Test, Repeat(IntegrationConfig.NumberOfRepeat)]
        public async Task ShouldRecoverFromFailerByUpdateMetadataOnce() //Do not debug this test !!
        {
            var log = new DefaultTraceLog();
            var routerProxy = new BrokerRouterProxy(_kernel);
            routerProxy._cacheExpiration = TimeSpan.FromMilliseconds(1000);
            var router = routerProxy.Create();

            int partitionId = 0;
            ProtocolGateway protocolGateway = new ProtocolGateway(router);
            var fetchRequest = new FetchRequest();

            int numberOfCall = 100;
            long numberOfErrorSend = 0;
            TaskCompletionSource<int> x = new TaskCompletionSource<int>();
            Func<Task<FetchResponse>> ShouldReturnNotLeaderForPartitionAndThenNoError = async () =>
            {
                log.DebugFormat("FetchResponse Start ");
                if (!x.Task.IsCompleted)
                {
                    if (Interlocked.Increment(ref numberOfErrorSend) == numberOfCall)
                    {
                        await Task.Delay(routerProxy._cacheExpiration);
                        await Task.Delay(1);
                        x.TrySetResult(1);
                        log.DebugFormat("all is complete ");
                    }

                    await x.Task;
                    log.DebugFormat("SocketException ");
                    throw new BrokerConnectionException("",new KafkaEndpoint());
                }
                log.DebugFormat("Completed ");

                return new FetchResponse() { Error = (short)ErrorResponseCode.NoError };
            };

            routerProxy.BrokerConn0.FetchResponseFunction = ShouldReturnNotLeaderForPartitionAndThenNoError;
            routerProxy.BrokerConn0.MetadataResponseFunction = BrokerRouterProxy.CreateMetadataResponseWithMultipleBrokers;

            Task[] tasks = new Task[numberOfCall];

            for (int i = 0; i < numberOfCall; i++)
            {
                tasks[i] = protocolGateway.SendProtocolRequest(fetchRequest, BrokerRouterProxy.TestTopic, partitionId);
            }

            await Task.WhenAll(tasks);
            Assert.That(numberOfErrorSend, Is.GreaterThan(1), "numberOfErrorSend");
            Assert.That(routerProxy.BrokerConn0.FetchRequestCallCount, Is.EqualTo(numberOfCall + numberOfErrorSend),
                "FetchRequestCallCount");
            Assert.That(routerProxy.BrokerConn0.MetadataRequestCallCount, Is.EqualTo(2), "MetadataRequestCallCount");
        }

        [Test, Repeat(IntegrationConfig.NumberOfRepeat)]
        public async Task ShouldRecoverFromFailerByUpdateMetadataOnceFullScenario() //Do not debug this test !!
        {
            var routerProxy = new BrokerRouterProxy(_kernel);
            routerProxy._cacheExpiration = TimeSpan.FromMilliseconds(0);
            var router = routerProxy.Create();
            int partitionId = 0;
            ProtocolGateway protocolGateway = new ProtocolGateway(router);
            var fetchRequest = new FetchRequest();

            CreateSuccessfulSendMock(routerProxy);

            //Send Successful Message
            await protocolGateway.SendProtocolRequest(fetchRequest, BrokerRouterProxy.TestTopic, partitionId);

            Assert.That(routerProxy.BrokerConn0.FetchRequestCallCount, Is.EqualTo(1), "FetchRequestCallCount");
            Assert.That(routerProxy.BrokerConn0.MetadataRequestCallCount, Is.EqualTo(1), "MetadataRequestCallCount");
            Assert.That(routerProxy.BrokerConn1.MetadataRequestCallCount, Is.EqualTo(0), "MetadataRequestCallCount");

            routerProxy.BrokerConn0.FetchResponseFunction = FailedInFirstMessageException(typeof(BrokerConnectionException), TimeSpan.Zero);
            //triger to update metadata
            routerProxy.BrokerConn0.MetadataResponseFunction = BrokerRouterProxy.CreateMetaResponseWithException;
            routerProxy.BrokerConn1.MetadataResponseFunction = BrokerRouterProxy.CreateMetadataResponseWithSingleBroker;

            //Reset variables
            routerProxy.BrokerConn0.FetchRequestCallCount = 0;
            routerProxy.BrokerConn1.FetchRequestCallCount = 0;
            routerProxy.BrokerConn0.MetadataRequestCallCount = 0;
            routerProxy.BrokerConn1.MetadataRequestCallCount = 0;

            //Send Successful Message that was recover from exception
            await protocolGateway.SendProtocolRequest(fetchRequest, BrokerRouterProxy.TestTopic, partitionId);

            Assert.That(routerProxy.BrokerConn0.FetchRequestCallCount, Is.EqualTo(1), "FetchRequestCallCount");
            Assert.That(routerProxy.BrokerConn0.MetadataRequestCallCount, Is.EqualTo(1), "MetadataRequestCallCount");

            Assert.That(routerProxy.BrokerConn1.FetchRequestCallCount, Is.EqualTo(1), "FetchRequestCallCount");
            Assert.That(routerProxy.BrokerConn1.MetadataRequestCallCount, Is.EqualTo(1), "MetadataRequestCallCount");
        }

        [Test, Repeat(IntegrationConfig.NumberOfRepeat)]
        public async Task ShouldRecoverFromFailerByUpdateMetadataOnceFullScenario1()
        {
            var routerProxy = new BrokerRouterProxy(_kernel);
            routerProxy._cacheExpiration = TimeSpan.FromMilliseconds(0);
            var router = routerProxy.Create();
            int partitionId = 0;
            ProtocolGateway protocolGateway = new ProtocolGateway(router);
            var fetchRequest = new FetchRequest();

            CreateSuccessfulSendMock(routerProxy);

            //Send Successful Message
            await protocolGateway.SendProtocolRequest(fetchRequest, BrokerRouterProxy.TestTopic, partitionId);

            Assert.That(routerProxy.BrokerConn0.FetchRequestCallCount, Is.EqualTo(1), "FetchRequestCallCount");
            Assert.That(routerProxy.BrokerConn0.MetadataRequestCallCount, Is.EqualTo(1), "MetadataRequestCallCount");
            Assert.That(routerProxy.BrokerConn1.MetadataRequestCallCount, Is.EqualTo(0), "MetadataRequestCallCount");

            routerProxy.BrokerConn0.FetchResponseFunction = FailedInFirstMessageError(ErrorResponseCode.LeaderNotAvailable, TimeSpan.Zero);

            routerProxy.BrokerConn0.MetadataResponseFunction = BrokerRouterProxy.CreateMetadataResponseWithSingleBroker;
            routerProxy.BrokerConn1.MetadataResponseFunction = BrokerRouterProxy.CreateMetadataResponseWithSingleBroker;

            //Reset variables
            routerProxy.BrokerConn0.FetchRequestCallCount = 0;
            routerProxy.BrokerConn1.FetchRequestCallCount = 0;
            routerProxy.BrokerConn0.MetadataRequestCallCount = 0;
            routerProxy.BrokerConn1.MetadataRequestCallCount = 0;

            //Send Successful Message that was recover from exception
            await protocolGateway.SendProtocolRequest(fetchRequest, BrokerRouterProxy.TestTopic, partitionId);

            Assert.That(routerProxy.BrokerConn0.FetchRequestCallCount, Is.EqualTo(1), "FetchRequestCallCount");
            Assert.That(routerProxy.BrokerConn0.MetadataRequestCallCount, Is.EqualTo(1), "MetadataRequestCallCount");

            Assert.That(routerProxy.BrokerConn1.FetchRequestCallCount, Is.EqualTo(1), "FetchRequestCallCount");
            Assert.That(routerProxy.BrokerConn1.MetadataRequestCallCount, Is.EqualTo(0), "MetadataRequestCallCount");
        }

        private static Func<Task<FetchResponse>> FailedInFirstMessageError(ErrorResponseCode errorResponseCode,
            TimeSpan delay)
        {
            bool firstTime = true;
            Func<Task<FetchResponse>> result = async () =>
            {
                if (firstTime)
                {
                    await Task.Delay(delay);
                    await Task.Delay(1);
                    firstTime = false;
                    return new FetchResponse() { Error = (short)errorResponseCode };
                }
                return new FetchResponse() { Error = (short)ErrorResponseCode.NoError };
            };
            return result;
        }

        private Func<Task<FetchResponse>> FailedInFirstMessageException(Type exceptionType, TimeSpan delay)
        {
            bool firstTime = true;
            Func<Task<FetchResponse>> result = async () =>
            {
                if (firstTime)
                {
                    await Task.Delay(delay);
                    await Task.Delay(1);
                    firstTime = false;
                    if (exceptionType == typeof(BrokerConnectionException))
                    {
                        throw new BrokerConnectionException("",new KafkaEndpoint());
                    }
                    object[] args = new object[1];
                    args[0] = "error Test";
                    throw (Exception)Activator.CreateInstance(exceptionType, args);
                }
                return new FetchResponse() { Error = (short)ErrorResponseCode.NoError };
            };
            return result;
        }

        private void CreateSuccessfulSendMock(BrokerRouterProxy routerProxy)
        {
            routerProxy.BrokerConn0.FetchResponseFunction = ShouldReturnValidMessage;
            routerProxy.BrokerConn0.MetadataResponseFunction = BrokerRouterProxy.CreateMetadataResponseWithMultipleBrokers;
            routerProxy.BrokerConn1.FetchResponseFunction = ShouldReturnValidMessage;
            routerProxy.BrokerConn1.MetadataResponseFunction = BrokerRouterProxy.CreateMetadataResponseWithMultipleBrokers;
        }

        private Task<FetchResponse> ShouldReturnValidMessage()
        {
            return Task.FromResult(new FetchResponse() { Error = (short)ErrorResponseCode.NoError });
        }
    }
}