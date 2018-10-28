using System;
using System.IO;
#if NETFX_CORE
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
#endif

namespace NongCrypto
{
    public abstract class CryptoHasher
    {
        public const int PageSize = 4096;

        public abstract string Name { get; }
        public abstract uint HashLength { get; }
        public abstract byte[] GetHashAndReset();
        public abstract void Append (byte[] data, int inputOffset, int inputCount);

        public void Append (byte[] data)
         => Append (data, 0, data.Length);

        public void Append (byte[] data, int offset1, int count1, int offset2, int count2)
        {
            if (count1 == 0 && count2 == 0)
                return;

            if (offset1 < 0)
                throw new ArgumentOutOfRangeException ("offset1", "Must be non-negative");

            if (offset2 < 0)
                throw new ArgumentOutOfRangeException ("offset2", "Must be non-negative");

            if (count1 < 0 || offset1 + count1 > data.Length)
                throw new ArgumentException ("Invalid value");

            if (count2 < 0 || offset2 + count2 > data.Length)
                throw new ArgumentException ("Invalid value");

            Append (data, offset1, count1);
            Append (data, offset2, count2);
        }

        public void Append (Stream stream)
         => Append (stream, 0, stream.Length);

        public void Append (Stream stream, long offset, long count)
        {
            if (count == 0)
                return;

            if (offset < 0)
                throw new ArgumentOutOfRangeException ("offset", "Must be non-negative");

            if (count < 0)
                throw new ArgumentOutOfRangeException ("count", "Must be non-negative");

            var buf = new byte[Math.Min (PageSize, count)];
            var stop = offset + count;

            stream.Position = offset;

            // Size the first read to align with a page boundary.
            int len = (int) (stop-offset < PageSize? (stop-offset) : (PageSize - offset % PageSize));

            while (stop - offset > PageSize)
            {
                if (stream.Read (buf, 0, len) != len)
                    throw new EndOfStreamException ("Read failed");

                Append (buf, 0, len);
                offset += len;
                len = PageSize;
            }

            len = (int) (stop-offset);
            if (len > 0)
                if (stream.Read (buf, 0, len) != len)
                    throw new EndOfStreamException ("Read failed");

            Append (buf, 0, len);
        }

        public void Append (Stream stream, long offset1, long count1, long offset2, long count2)
        {
            var buf = new byte[PageSize];
            long stop1 = offset1 + count1,
                 stop2 = offset2 + count2;

            if (offset1 < 0)
                throw new ArgumentOutOfRangeException ("offset1", "Must be non-negative");

            if (offset2 < 0)
                throw new ArgumentOutOfRangeException ("offset2", "Must be non-negative");

            stream.Position = offset1;

            // Size the first read to align with a page boundary.
            int len = (int) (stop1-offset1 < PageSize? stop1-offset1 : PageSize - offset1%PageSize);

            while (stop1 - offset1 > PageSize)
            {
                if (stream.Read (buf, 0, len) != len)
                    throw new EndOfStreamException ("Read failed");
                Append (buf, 0, len);
                offset1 += len;
                len = PageSize;
            }

            len = (int) (stop1 - offset1);
            if (len > 0)
            {
                if (stream.Read (buf, 0, len) != len)
                    throw new EndOfStreamException ("Read failed");
                Append (buf, 0, len);
                offset1 += len;
            }

            stream.Position = offset2;

            len = (int) (stop2-offset2 < PageSize? stop2-offset2 : PageSize - offset2%PageSize);
            while (stop2 - offset2 > PageSize)
            {
                if (stream.Read (buf, 0, len) != len)
                    throw new EndOfStreamException ("Read failed");

                Append (buf, 0, len);
                offset2 += len;
                len = PageSize;
            }

            len = (int) (stop2 - offset2);
            if (len > 0)
            {
                if (stream.Read (buf, 0, len) != len)
                    throw new EndOfStreamException ("Read failed");
                Append (buf, 0, len);
            }
        }

        public void Append (BinaryReader reader)
        {
            int got = 0;
            var buf = new byte[PageSize];

            try
            {
                for (;;)
                {
                    do
                    {
                        buf[got] = reader.ReadByte();
                        ++got;
                    } while (got < PageSize);

                    Append (buf, 0, PageSize);
                    got = 0;
                }
            }
            catch (EndOfStreamException)
            {
                Append (buf, 0, got);
            }
        }
    }
#if NETFX_CORE
    public abstract class CryptoCoreHasher : CryptoHasher
    {
        protected HashAlgorithmProvider provider;
        protected CryptographicHash hasher;

        public override string Name => provider.AlgorithmName;
        public override uint HashLength => provider.HashLength;

        public override void Append (byte[] data, int offset, int count)
        {
            if (count == 0)
                return;

            IBuffer buf = WindowsRuntimeBufferExtensions.AsBuffer (data, offset, count);
            hasher.Append (buf);
        }

        public override byte[] GetHashAndReset()
        {
            IBuffer result = hasher.GetValueAndReset();
            var asBytes = WindowsRuntimeBufferExtensions.ToArray (result);
            return asBytes;
        }
    }
#else
    public abstract class CryptoFullHasher : CryptoHasher
    {
        protected System.Security.Cryptography.HashAlgorithm hasher;

        public override uint HashLength => (uint) hasher.HashSize / 8;

        public override void Append (byte[] data, int offset, int count)
        {
            if (count != 0)
                hasher.TransformBlock (data, offset, count, data, offset);
        }

        static readonly byte[] bb0 = new byte[0];
        public override byte[] GetHashAndReset()
        {
            hasher.TransformFinalBlock (bb0, 0, 0);
            var result = hasher.Hash;
            hasher.Initialize();
            return result;
        }
    }
#endif
}
