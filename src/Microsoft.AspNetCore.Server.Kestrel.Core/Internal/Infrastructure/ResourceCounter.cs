// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure
{
    public abstract class ResourceCounter
    {
        public abstract bool TryLockOne();
        public abstract void ReleaseOne();

        public static ResourceCounter Unlimited { get; } = new UnlimitedCounter();
        public static ResourceCounter Quota(long amount) => new FiniteCounter(amount);

        private class UnlimitedCounter : ResourceCounter
        {
            public override bool TryLockOne() => true;
            public override void ReleaseOne()
            {
            }
        }

        internal class FiniteCounter : ResourceCounter
        {
            private readonly long _max;
            private long _count;

            public FiniteCounter(long max)
            {
                if (max < 0)
                {
                    throw new ArgumentOutOfRangeException(CoreStrings.NonNegativeNumberRequired);
                }

                _max = max;
            }

            public override bool TryLockOne()
            {
                var next = Interlocked.Increment(ref _count);
                // check that it's positive to avoid overflow
                var accepted = next <= _max && next > 0;
                if (!accepted)
                {
                    Interlocked.Decrement(ref _count);
                }
                return accepted;
            }

            public override void ReleaseOne()
            {
                Interlocked.Decrement(ref _count);

                Debug.Assert(_count >= 0, "Resource count is negative. More resources were released than were locked.");
            }

            // for testing
            internal long Count
            {
                get => _count;
                set => _count = value;
            }
        }
    }
}
