﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using d60.Cirqus.Events;
using d60.Cirqus.Extensions;
using d60.Cirqus.Logging;
using Microsoft.ServiceBus;

namespace d60.Cirqus.AzureServiceBus.Relay
{
    public class AzureServiceBusRelayEventStoreFacade : IEventStore, IDisposable
    {
        static Logger _logger;

        static AzureServiceBusRelayEventStoreFacade()
        {
            CirqusLoggerFactory.Changed += f => _logger = f.GetCurrentClassLogger();
        }

        readonly Serializer _serializer = new Serializer();
        readonly ChannelFactory<IHostService> _channelFactory;
        
        IHostService _currentClientChannel;
        bool _disposed;

        public AzureServiceBusRelayEventStoreFacade(string serviceNamespace, string servicePath, string keyName, string sharedAccessKey)
        {
            var uri = ServiceBusEnvironment.CreateServiceUri("sb", serviceNamespace, servicePath);

            _logger.Info("Initializing event store facade for {0}", uri);
            
            var binding = new NetTcpRelayBinding();
            _channelFactory = new ChannelFactory<IHostService>(binding, new EndpointAddress(uri));

            var tokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(keyName, sharedAccessKey);
            var endpointBehavior = new TransportClientEndpointBehavior(tokenProvider);
            _channelFactory.Endpoint.Behaviors.Add(endpointBehavior);
        }

        ~AzureServiceBusRelayEventStoreFacade()
        {
            Dispose(false);
        }

        public void Save(Guid batchId, IEnumerable<DomainEvent> batch)
        {
            throw new InvalidOperationException();
        }

        public long GetNextGlobalSequenceNumber()
        {
            throw new InvalidOperationException();
        }

        public IEnumerable<DomainEvent> Load(Guid aggregateRootId, long firstSeq = 0)
        {
            var client = GetClient();

            while (true)
            {
                var transportMessage = client.Load(aggregateRootId, firstSeq);
                var domainEvents = _serializer.Deserialize(transportMessage.Events);

                foreach (var e in domainEvents)
                {
                    yield return e;
                }
            }
        }

        public IEnumerable<DomainEvent> Stream(long globalSequenceNumber = 0)
        {
            var client = GetClient();
            var currentPosition = globalSequenceNumber;

            while (true)
            {
                var transportMessage = client.Stream(currentPosition);
                var domainEvents = _serializer.Deserialize(transportMessage.Events);

                if (!domainEvents.Any())
                {
                    yield break;
                }

                foreach (var e in domainEvents)
                {
                    yield return e;

                    currentPosition = e.GetGlobalSequenceNumber() + 1;
                }
            }
        }

        IHostService GetClient()
        {
            if (_currentClientChannel != null)
            {
                return _currentClientChannel;
            }

            _currentClientChannel = _channelFactory.CreateChannel();
            
            return _currentClientChannel;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_disposed) return;

                try
                {
                    _logger.Info("Disposing channel factory");
                    _channelFactory.Close(TimeSpan.FromSeconds(10));
                }
                catch (Exception exception)
                {
                    _logger.Warn(exception, "An error occurred while closing the channel factory");
                }
                finally
                {
                    _disposed = true;
                }
            }
        }
    }
}