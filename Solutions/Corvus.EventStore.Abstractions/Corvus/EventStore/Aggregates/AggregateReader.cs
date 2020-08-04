﻿// <copyright file="AggregateReader.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Corvus.EventStore.Aggregates
{
    using System;
    using System.Threading.Tasks;
    using Corvus.EventStore.Core;

    /// <summary>
    /// Reads aggregate roots using a combination of snapshot and events.
    /// </summary>
    /// <typeparam name="TEventReader">The type of the underlying event reader.</typeparam>
    /// <typeparam name="TSnapshotReader">The type of the underlying snapshot reader.</typeparam>
    /// <typeparam name="TSnapshot">The underlying snapshot type created by the snapshot reader implementation.</typeparam>
    /// <typeparam name="TMemento">The memento type used by the aggregate that can be read by this reader.</typeparam>
    public readonly struct AggregateReader<TEventReader, TSnapshotReader, TSnapshot, TMemento>
        where TEventReader : IEventReader
        where TSnapshotReader : ISnapshotReader<TSnapshot, TMemento>
        where TSnapshot : ISnapshot<TMemento>
    {
        private readonly TEventReader eventReader;
        private readonly TSnapshotReader snapshotReader;

        /// <summary>
        /// Initializes a new instance of the <see cref="AggregateReader{TEventReader, TSnapshotReader, TSnapshot, TMemento}"/> struct.
        /// </summary>
        /// <param name="eventReader">The underlying event reader.</param>
        /// <param name="snapshotReader">The underlying snapshot reader.</param>
        public AggregateReader(TEventReader eventReader, TSnapshotReader snapshotReader)
        {
            this.eventReader = eventReader;
            this.snapshotReader = snapshotReader;
        }

        /// <summary>
        /// Reads the aggregate with the specified Id.
        /// </summary>
        /// <typeparam name="TAggregate">The type of aggregate being read.</typeparam>
        /// <param name="aggregateFactory">A factory method for creating an aggregate from the supplied memento.</param>
        /// <param name="defaultPayloadFactory">A factory method for creating a default snapshot payload if one cannot be found.</param>
        /// <param name="aggregateId">The Id of the aggregate.</param>
        /// <param name="sequenceNumber">The maximum sequence number to retrieve.</param>
        /// <returns>The specified aggregate.</returns>
        /// <remarks>
        /// If a sequence number is supplied, the aggregate will be recreated using events with sequence numbers lower
        /// than or equal to the specified value.
        /// </remarks>
        public async ValueTask<TAggregate> ReadAsync<TAggregate>(
            Func<TSnapshot, TAggregate> aggregateFactory,
            Func<TMemento> defaultPayloadFactory,
            string aggregateId,
            long sequenceNumber = long.MaxValue)
            where TAggregate : IAggregateRoot<TAggregate>
        {
            TSnapshot snapshot = await this.snapshotReader.ReadAsync(defaultPayloadFactory, aggregateId, sequenceNumber).ConfigureAwait(false);
            TAggregate aggregate = aggregateFactory(snapshot);

            if (aggregate.SequenceNumber < sequenceNumber)
            {
                IEventReaderResult newEvents = await this.eventReader.ReadAsync(
                    aggregate.AggregateId,
                    aggregate.SequenceNumber + 1,
                    sequenceNumber,
                    int.MaxValue).ConfigureAwait(false);

                while (true)
                {
                    foreach (IEvent @event in newEvents.Events)
                    {
                        aggregate = aggregate.ApplyEvent(@event);
                    }

                    if (string.IsNullOrEmpty(newEvents.ContinuationToken))
                    {
                        break;
                    }

                    newEvents = await this.eventReader.ReadAsync(newEvents.ContinuationToken).ConfigureAwait(false);
                }
            }

            return aggregate;
        }
    }
}
