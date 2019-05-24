﻿/* Copyright 2013-present MongoDB Inc.
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
* http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SAEA.Mongo.Bson;
using SAEA.Mongo.Bson.Serialization.Serializers;
using SAEA.Mongo.Driver.Core.Bindings;
using SAEA.Mongo.Driver.Core.Events;
using SAEA.Mongo.Driver.Core.Misc;
using SAEA.Mongo.Driver.Core.WireProtocol.Messages.Encoders;

namespace SAEA.Mongo.Driver.Core.Operations
{
    /// <summary>
    /// Represents a list collections operation.
    /// </summary>
    public class ListCollectionsUsingCommandOperation : IReadOperation<IAsyncCursor<BsonDocument>>, IExecutableInRetryableReadContext<IAsyncCursor<BsonDocument>>
    {
        // fields
        private BsonDocument _filter;
        private readonly DatabaseNamespace _databaseNamespace;
        private readonly MessageEncoderSettings _messageEncoderSettings;
        private bool? _nameOnly;
        private bool _retryRequested;

        // constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ListCollectionsOperation"/> class.
        /// </summary>
        /// <param name="databaseNamespace">The database namespace.</param>
        /// <param name="messageEncoderSettings">The message encoder settings.</param>
        public ListCollectionsUsingCommandOperation(
            DatabaseNamespace databaseNamespace,
            MessageEncoderSettings messageEncoderSettings)
        {
            _databaseNamespace = Ensure.IsNotNull(databaseNamespace, nameof(databaseNamespace));
            _messageEncoderSettings = Ensure.IsNotNull(messageEncoderSettings, nameof(messageEncoderSettings));
        }

        // properties
        /// <summary>
        /// Gets or sets the filter.
        /// </summary>
        /// <value>
        /// The filter.
        /// </value>
        public BsonDocument Filter
        {
            get { return _filter; }
            set { _filter = value; }
        }

        /// <summary>
        /// Gets the database namespace.
        /// </summary>
        /// <value>
        /// The database namespace.
        /// </value>
        public DatabaseNamespace DatabaseNamespace
        {
            get { return _databaseNamespace; }
        }

        /// <summary>
        /// Gets the message encoder settings.
        /// </summary>
        /// <value>
        /// The message encoder settings.
        /// </value>
        public MessageEncoderSettings MessageEncoderSettings
        {
            get { return _messageEncoderSettings; }
        }

        /// <summary>
        /// Gets or sets the name only option.
        /// </summary>
        /// <value>
        /// The name only option.
        /// </value>
        public bool? NameOnly
        {
            get { return _nameOnly; }
            set { _nameOnly = value; }
        }

        /// <summary>
        /// Gets or sets whether or not retry was requested.
        /// </summary>
        /// <value>
        /// Whether retry was requested.
        /// </value>
        public bool RetryRequested
        {
            get { return _retryRequested; }
            set { _retryRequested = value; }
        }

        // public methods
        /// <inheritdoc/>
        public IAsyncCursor<BsonDocument> Execute(IReadBinding binding, CancellationToken cancellationToken)
        {
            Ensure.IsNotNull(binding, nameof(binding));

            using (var context = RetryableReadContext.Create(binding, _retryRequested, cancellationToken))
            {
                return Execute(context, cancellationToken);
            }
        }

        /// <inheritdoc/>
        public IAsyncCursor<BsonDocument> Execute(RetryableReadContext context, CancellationToken cancellationToken)
        {
            Ensure.IsNotNull(context, nameof(context));

            using (EventContext.BeginOperation())
            {
                var operation = CreateOperation();
                var result = operation.Execute(context, cancellationToken);
                return CreateCursor(context.ChannelSource, operation.Command, result);
            }
        }

        /// <inheritdoc/>
        public async Task<IAsyncCursor<BsonDocument>> ExecuteAsync(IReadBinding binding, CancellationToken cancellationToken)
        {
            Ensure.IsNotNull(binding, nameof(binding));

            using (var context = await RetryableReadContext.CreateAsync(binding, _retryRequested, cancellationToken).ConfigureAwait(false))
            {
                return Execute(context, cancellationToken);
            }
        }

        /// <inheritdoc/>
        public async Task<IAsyncCursor<BsonDocument>> ExecuteAsync(RetryableReadContext context, CancellationToken cancellationToken)
        {
            Ensure.IsNotNull(context, nameof(context));

            using (EventContext.BeginOperation())
            {
                var operation = CreateOperation();
                var result = await operation.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
                return CreateCursor(context.ChannelSource, operation.Command, result);
            }
        }

        // private methods
        private ReadCommandOperation<BsonDocument> CreateOperation()
        {
            var command = new BsonDocument
            {
                { "listCollections", 1 },
                { "filter", _filter, _filter != null },
                { "nameOnly", () => _nameOnly.Value, _nameOnly.HasValue }
            };
            return new ReadCommandOperation<BsonDocument>(_databaseNamespace, command, BsonDocumentSerializer.Instance, _messageEncoderSettings)
            {
                RetryRequested = _retryRequested // might be overridden by retryable read context
            };
        }

        private IAsyncCursor<BsonDocument> CreateCursor(IChannelSourceHandle channelSource, BsonDocument command, BsonDocument result)
        {
            var getMoreChannelSource = new ServerChannelSource(channelSource.Server, channelSource.Session.Fork());
            var cursorDocument = result["cursor"].AsBsonDocument;
            var cursor = new AsyncCursor<BsonDocument>(
                getMoreChannelSource,
                CollectionNamespace.FromFullName(cursorDocument["ns"].AsString),
                command,
                cursorDocument["firstBatch"].AsBsonArray.OfType<BsonDocument>().ToList(),
                cursorDocument["id"].ToInt64(),
                0,
                0,
                BsonDocumentSerializer.Instance,
                _messageEncoderSettings);

            return cursor;
        }
    }
}