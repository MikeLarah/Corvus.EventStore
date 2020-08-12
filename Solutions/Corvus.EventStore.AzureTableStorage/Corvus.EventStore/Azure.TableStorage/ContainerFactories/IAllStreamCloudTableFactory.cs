﻿// <copyright file="IAllStreamCloudTableFactory.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Corvus.EventStore.Azure.TableStorage.ContainerFactories
{
    using Microsoft.Azure.Cosmos.Table;

    /// <summary>
    /// A container factory to provide the <see cref="CloudTable"/> for a particular
    /// aggregate.
    /// </summary>
    public interface IAllStreamCloudTableFactory
    {
        /// <summary>
        /// Gets an instance of the cloud table for the given aggregate ID and logical partition Key.
        /// </summary>
        /// <returns>The cloud table for that partition and aggregate.</returns>
        CloudTable GetTable();
    }
}