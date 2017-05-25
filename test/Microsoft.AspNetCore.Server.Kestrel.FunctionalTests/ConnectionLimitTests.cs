// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Tests;
using Microsoft.AspNetCore.Testing;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.FunctionalTests
{
    public class ConnectionLimitTests
    {
        private static async Task KeepOpen(HttpContext context)
        {
            // begin the response
            await context.Response.WriteAsync("Hello");

            // keep it running until abored
            while (!context.RequestAborted.IsCancellationRequested)
            {
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task ResetsCountWhenConnectionClosed()
        {
            var releasedTcs = new TaskCompletionSource<object>();
            var lockedTcs = new TaskCompletionSource<bool>();
            var (context, counter) = SetupMaxConnections(max: 1);
            counter.OnLock += (s, e) => lockedTcs.TrySetResult(e);
            counter.OnRelease += (s, e) => releasedTcs.TrySetResult(null);

            using (var server = new TestServer(KeepOpen, context))
            using (var connection = server.CreateConnection())
            {
                await connection.SendEmptyGetAsKeepAlive(); ;
                await connection.Receive("HTTP/1.1 200 OK");
                Assert.True(await lockedTcs.Task.TimeoutAfter(TimeSpan.FromSeconds(10)));
            }

            await releasedTcs.Task.TimeoutAfter(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task RejectsConnectionsWhenLimitReached()
        {
            const int max = 10;
            var (context, _) = SetupMaxConnections(max);

            using (var server = new TestServer(KeepOpen, context))
            using (var disposables = new DisposableStack<TestConnection>())
            {
                for (var i = 0; i < max; i++)
                {
                    var connection = server.CreateConnection();
                    disposables.Push(connection);

                    await connection.SendEmptyGetAsKeepAlive();
                    await connection.Receive("HTTP/1.1 200 OK");
                }

                // limit has been reached
                for (var i = 0; i < 10; i++)
                {
                    using (var connection = server.CreateConnection())
                    {
                        try
                        {
                            await connection.SendEmptyGetAsKeepAlive();
                        } catch { }

                        // connection should close with not data sent in return
                        await connection.WaitForConnectionClose().TimeoutAfter(TimeSpan.FromSeconds(15));
                    }
                }
            }
        }

        [Fact]
        public async Task ConnectionCountingReturnsToZero()
        {
            const int count = 500;
            var opened = 0;
            var closed = 0;
            var openedTcs = new TaskCompletionSource<object>();
            var closedTcs = new TaskCompletionSource<object>();

            var (context, counter) = SetupMaxConnections(uint.MaxValue);

            counter.OnLock += (o, e) =>
            {
                if (e && Interlocked.Increment(ref opened) >= count)
                {
                    openedTcs.TrySetResult(null);
                }
            };

            counter.OnRelease += (o, e) =>
            {
                if (Interlocked.Increment(ref closed) >= count)
                {
                    closedTcs.TrySetResult(null);
                }
            };

            using (var server = new TestServer(_ => Task.CompletedTask, context))
            {
                // open a bunch of connections in parallel
                Parallel.For(0, count, async i =>
                {
                    try
                    {
                        using (var connection = server.CreateConnection())
                        {
                            await connection.SendEmptyGetAsKeepAlive();
                            await connection.Receive("HTTP/1.1 200");
                        }
                    }
                    catch (Exception ex)
                    {
                        closedTcs.TrySetException(ex);
                    }
                });

                // wait until resource counter has called lock for each connection
                await openedTcs.Task.TimeoutAfter(TimeSpan.FromSeconds(60));
                // wait until resource counter has released all normal connections
                await closedTcs.Task.TimeoutAfter(TimeSpan.FromSeconds(60));
                Assert.Equal(count, opened);
                Assert.Equal(count, closed);
            }
        }

        private (TestServiceContext context, EventRaisingResourceCounter counter) SetupMaxConnections(long max)
        {
            var counter = new EventRaisingResourceCounter(ResourceCounter.Quota(max));
            var context = new TestServiceContext
            {
                Resources = new ResourceManager(counter, ResourceCounter.Unlimited)
            };
            return (context, counter);
        }
    }
}
