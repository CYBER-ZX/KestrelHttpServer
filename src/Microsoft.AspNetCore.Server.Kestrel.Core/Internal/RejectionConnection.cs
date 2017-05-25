// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal
{
    public class RejectionConnection : IConnectionContext
    {
        private readonly IKestrelTrace _log;

        public RejectionConnection(IPipe input, IPipe output, string connectionId, ServiceContext serviceContext)
        {
            _log = serviceContext.Log;
            ConnectionId = connectionId;
            Input = input.Writer;
            Output = output.Reader;
        }

        public string ConnectionId { get; }
        public IPipeWriter Input { get; }
        public IPipeReader Output { get; }

        public void Reject()
        {
            KestrelEventSource.Log.ConnectionRejected(ConnectionId);
            _log.ConnectionRejected(ConnectionId);

            Abort(new ConnectionAbortedException());
        }

        // TODO: Remove these (https://github.com/aspnet/KestrelHttpServer/issues/1772)
        public void OnConnectionClosed(Exception ex)
        {
        }

        public void Abort(Exception ex)
        {
            Input.CancelPendingFlush();
            Output.CancelPendingRead();
            Input.Complete(ex);
            Output.Complete(ex);
        }
    }
}
