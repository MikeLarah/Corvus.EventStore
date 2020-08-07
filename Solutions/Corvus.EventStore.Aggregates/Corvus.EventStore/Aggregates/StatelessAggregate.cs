﻿// <copyright file="StatelessAggregate.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Corvus.EventStore.Aggregates
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Threading.Tasks;
    using Corvus.EventStore.Core;
    using Corvus.EventStore.Serialization;
    using Corvus.EventStore.Serialization.Json;
    using Corvus.EventStore.Snapshots;

    /// <summary>
    /// An implementation of an aggregate root that has no internal state beyond its sequence numbers and commit information.
    /// </summary>
    /// <remarks>This would typically be used by an aggregate that raises events to update external stores in other services.</remarks>
    public readonly struct StatelessAggregate : IAggregateRoot<StatelessAggregate>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AggregateWithMemento{Task, TMemento}"/> struct.
        /// </summary>
        /// <param name="aggregateId">The <see cref="AggregateId"/>.</param>
        /// <param name="partitionKey">The <see cref="PartitionKey"/>.</param>
        /// <param name="commitSequenceNumber">The <see cref="CommitSequenceNumber"/>.</param>
        /// <param name="eventSequenceNumber">The <see cref="EventSequenceNumber"/>.</param>
        /// <param name="uncommittedEvents">The <see cref="UncommittedEvents"/>.</param>
        private StatelessAggregate(Guid aggregateId, string partitionKey, long commitSequenceNumber, long eventSequenceNumber, in ImmutableArray<SerializedEvent> uncommittedEvents)
        {
            this.AggregateId = aggregateId;
            this.PartitionKey = partitionKey;
            this.CommitSequenceNumber = commitSequenceNumber;
            this.EventSequenceNumber = eventSequenceNumber;
            this.UncommittedEvents = uncommittedEvents;
        }

        /// <summary>
        /// Gets or sets the event serializer to use for the aggregate.
        /// </summary>
        public static IEventSerializer EventSerializer { get; set; } = new Utf8JsonEventSerializer(Utf8JsonEventSerializer.DefaultOptions);

        /// <summary>
        /// Gets or sets the snapshot serializer to use for the aggregate.
        /// </summary>
        public static ISnapshotSerializer SnapshotSerializer { get; set; } = new Utf8JsonSnapshotSerializer(Utf8JsonSnapshotSerializer.DefaultOptions);

        /// <summary>
        /// Gets the unique Id for the aggregate.
        /// </summary>
        public Guid AggregateId { get; }

        /// <summary>
        /// Gets the partition key for the aggregate.
        /// </summary>
        /// <remarks>
        /// For most implementations, the AggregateId makes a good partition key.
        /// </remarks>
        public string PartitionKey { get; }

        /// <summary>
        /// Gets the sequence number of the current commit for the aggregate.
        /// </summary>
        /// <remarks>
        /// This represents the last commit before any currently uncommitted events were added.
        /// </remarks>
        public long CommitSequenceNumber { get; }

        /// <summary>
        /// Gets the sequence number of the latest event in the aggregate, including uncommitted events.
        /// </summary>
        public long EventSequenceNumber { get; }

        /// <summary>
        /// Gets the uncommitted events.
        /// </summary>
        public ImmutableArray<SerializedEvent> UncommittedEvents { get; }

        /// <summary>
        /// Reads an instance of an aggregate, optionally to the specified commit sequence number.
        /// </summary>
        /// <typeparam name="TReader">The type of the <see cref="IAggregateReader"/>.</typeparam>
        /// <param name="reader">The reader from which to read the aggregate.</param>
        /// <param name="aggregateId">The id of the aggregate to read.</param>
        /// <param name="partitionKey">The partition key of the aggregate.</param>
        /// <param name="maxItemsPerBatch">The optional maximum number of items per batch. The default is 100.</param>
        /// <param name="commitSequenceNumber">The (optional) commit sequence number at which to read the aggregate.</param>
        /// <returns>A <see cref="ValueTask"/> which completes with the aggregate.</returns>
        public static ValueTask<StatelessAggregate> ReadAsync<TReader>(TReader reader, Guid aggregateId, string partitionKey, int maxItemsPerBatch = 100, long commitSequenceNumber = long.MaxValue)
            where TReader : IAggregateReader
        {
            return reader.ReadAsync(s => CreateFrom(s), aggregateId, partitionKey, maxItemsPerBatch, commitSequenceNumber);
        }

        /// <summary>
        /// Applies the given event to the aggregate.
        /// </summary>
        /// <typeparam name="TPayload">The payload of the event to apply.</typeparam>
        /// <param name="event">The event to apply.</param>
        /// <remarks>
        /// This will be called when a new event has been created.
        /// </remarks>
        /// <returns>The aggregate with the event applied.</returns>
        public StatelessAggregate ApplyEvent<TPayload>(in Event<TPayload> @event)
        {
            this.Validate(@event);
            SerializedEvent serializedEvent = EventSerializer.Serialize(@event);
            return new StatelessAggregate(this.AggregateId, this.PartitionKey, this.CommitSequenceNumber, this.EventSequenceNumber + 1, this.UncommittedEvents.Add(serializedEvent));
        }

        /// <summary>
        /// Applies the given commits to the aggregate.
        /// </summary>
        /// <param name="commits">The ordered list of commits to apply to the aggregate.</param>
        /// <returns>The aggreagte with the commits applied.</returns>
        /// <remarks>
        /// This will be called when the aggregate is being rehydrated from committed events.
        /// </remarks>
        public StatelessAggregate ApplyCommits(in IEnumerable<Commit> commits)
        {
            commits.ValidateCommits(this.AggregateId, this.CommitSequenceNumber, this.EventSequenceNumber);

            int eventCount = 0;
            int commitCount = 0;

            foreach (Commit commit in commits)
            {
                foreach (SerializedEvent @event in commit.Events)
                {
                    eventCount += 1;
                }

                commitCount += 1;
            }

            return new StatelessAggregate(this.AggregateId, this.PartitionKey, this.CommitSequenceNumber + commitCount, this.EventSequenceNumber + eventCount, this.UncommittedEvents);
        }

        /// <summary>
        /// Stores uncommitted events using the specified event writer.
        /// </summary>
        /// <typeparam name="TEventWriter">The type of event writer to use.</typeparam>
        /// <param name="writer">The event writer to use to store new events.</param>
        /// <returns>The aggregate with all new events committed.</returns>
        public async ValueTask<StatelessAggregate> CommitAsync<TEventWriter>(TEventWriter writer)
            where TEventWriter : IEventWriter
        {
            if (this.UncommittedEvents.Length == 0)
            {
                return this;
            }

            await writer.WriteCommitAsync(new Commit(this.AggregateId, this.PartitionKey, this.CommitSequenceNumber + 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), this.UncommittedEvents)).ConfigureAwait(false);
            return new StatelessAggregate(this.AggregateId, this.PartitionKey, this.CommitSequenceNumber + 1, this.EventSequenceNumber, ImmutableArray<SerializedEvent>.Empty);
        }

        /// <inheritdoc/>
        public Task StoreSnapshotAsync<TSnapshotWriter>(TSnapshotWriter writer)
            where TSnapshotWriter : ISnapshotWriter
        {
            var snapshot = new Snapshot<EmptyMemento>(this.AggregateId, this.PartitionKey, this.CommitSequenceNumber, this.EventSequenceNumber, EmptyMemento.Instance);
            return writer.WriteAsync(SnapshotSerializer.Serialize(snapshot));
        }

        /// <summary>
        /// Creates an instance of the implementation from a snapshot.
        /// </summary>
        /// <param name="snapshot">The <see cref="SerializedSnapshot"/> from which to create the state.</param>
        /// <returns>The state with the snapshot applied.</returns>
        private static StatelessAggregate CreateFrom(SerializedSnapshot snapshot)
        {
            if (snapshot.IsEmpty)
            {
                return new StatelessAggregate(snapshot.AggregateId, snapshot.PartitionKey, -1, -1, ImmutableArray<SerializedEvent>.Empty);
            }

            return new StatelessAggregate(
                snapshot.AggregateId,
                snapshot.PartitionKey,
                snapshot.CommitSequenceNumber,
                snapshot.EventSequenceNumber,
                ImmutableArray<SerializedEvent>.Empty);
        }

        private void Validate<TPayload>(in Event<TPayload> @event)
        {
            if (@event.SequenceNumber != this.EventSequenceNumber + 1)
            {
                throw new ArgumentException($"The event sequence number was incorrect. Expected {this.EventSequenceNumber + 1}, actual {@event.SequenceNumber}");
            }
        }

        private readonly struct EmptyMemento
        {
            public static readonly EmptyMemento Instance = default;
        }
    }
}