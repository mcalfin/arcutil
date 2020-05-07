using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace arcunpack
{
    class LZDecompress
    {
        private bool eof = false;
        private byte[] ring = new byte[0x1000];
        private int pos = 0;
        private int copyPos = 0;
        private int copyLen = 0;
        private int flags = 1;
        Stream input;

        public LZDecompress(byte[] data)
        {
            input = new MemoryStream(data);
        }

        public int read()
        {
            int b;
            int flag;

            if (eof)
            {
                return -1;
            }

            if (copyLen > 0)
            {
                b = ring[copyPos] & 0xFF;

                copyPos = (copyPos + 1) & 0xFFF;
                copyLen--;
            }
            else
            {
                flag = nextFlag();

                if (flag == 1)
                {
                    b = input.ReadByte();
                }
                else if (flag == 0)
                {
                    b = readBackRef();
                }
                else
                {
                    b = -1;
                }
            }

            if (b != -1)
            {
                ring[pos] = (byte)b;
                pos = (pos + 1) & 0xFFF;

                return b;
            }
            else
            {
                return -1;
            }
        }

        private int nextFlag()
        {
            if (flags == 1)
            {
                int r = input.ReadByte();

                if (r == -1)
                {
                    return -1;
                }
                else
                {
                    flags = 0x100 | r;
                }
            }

            int flag = flags & 1;
            flags = flags >> 1;

            return flag;
        }

        private int readBackRef()
        {
            int hi = input.ReadByte();

            if (hi == -1)
            {
                return -1;
            }

            int lo = input.ReadByte();

            if (hi == -1)
            {
                throw new IOException("Unexpected EOF mid-backref");
            }

            copyLen = (lo & 0x0F);
            copyPos = (hi << 4) | (lo >> 4);

            if (copyPos > 0)
            {
                copyLen += 3;
                copyPos = (pos - copyPos) & 0xFFF;

                int b = ring[copyPos] & 0xFF;

                copyPos = (copyPos + 1) & 0xFFF;
                copyLen--;

                return b;
            }
            else
            {
                eof = true;

                return -1;
            }
        }


        public int read(byte[] b, int off, int len)
        {
            int i;

            for (i = 0; i < len; i++)
            {
                int r = read();

                if (r != -1)
                {
                    b[off + i] = (byte)r;
                }
                else
                {
                    break;
                }
            }

            return i;
        }

        public int read(byte[] b)
        {
            return read(b, 0, b.Count());
        }

        public long skip(long n)
        {
            long i;

            for (i = 0; i < n; i++)
            {
                if (read() == -1)
                {
                    break;
                }
            }

            return i;
        }

        public byte[] decompress()
        {
            MemoryStream outBytes = new MemoryStream();

            while (true)
            {
                int oByte = read();
                if (oByte == -1)
                    break;
                outBytes.WriteByte(Convert.ToByte(oByte));
            }
            return outBytes.ToArray();
        }
    }


    class LZCompress
    {
        private byte[] ring = new byte[0x1000];

        private static int MIN_MATCH = 3;
        private static int MAX_MATCH = MIN_MATCH + 15;
        private static short NULL = -1;

        private short[] links = new short[0x1000];
        private short[] heads = new short[0x100];
        private short[] tails = new short[0x100];
        private short[] cursors = new short[0x1000];

        private byte[] match = new byte[MAX_MATCH];
        private byte[] packet = new byte[16 * 2];

        private int ringPos = 0;
        private int ncursors = 0;
        private int nmatched = 0;
        private int pktPos = 0;
        private int flags = 0x10000;
        Stream output;

        public LZCompress()
        {
            output = new MemoryStream();

            for (int i = 0; i < 0x100; i++)
            {
                heads[i] = NULL;
                tails[i] = NULL;
            }
        }

        public Stream outputdata()
        {
            return output;
        }

        public void close()
        {

            emitMatch();

            // We need to send a <0, 0> backref here to indicate end of file.

            flags = (flags >> 1);
            packet[pktPos++] = 0;
            packet[pktPos++] = 0;

            // Bitshift until all the flag bits are in the right place.
            // Every flag to the left of the zero flag for the magic backref
            // is irrelevant, so we may as well make those zeros.

            while ((flags & 0x100) == 0)
            {
                if (flags == 0)
                {
                    Console.WriteLine("Error!");
                }

                flags = (flags >> 1);
            }

            // Write out our fancy final packet and close up.

            output.WriteByte((byte)(flags & 0xFF));
            output.Write(packet, 0, pktPos);

            output.Flush();

        }

        public void write(byte[] b, int off, int len)
        {
            for (int i = 0; i < len; i++)
            {
                write(b[off + i] & 0xFF);
            }
        }


        public void write(byte[] b)
        {
            write(b, 0, b.Length);
        }

        public void write(int b)
        {
            if (b < 0 || b > 255)
            {
                return;
            }
            if (nmatched == 0)
            {

                initCursors(b);
            }
            else if (nmatched == MAX_MATCH)
            {
                emitMatch();
                initCursors(b);
            }
            else
            {
                updateCursors(b);
            }

            advance(b);
        }

        private void advance(int newValue)
        {
            // Get previous symbol at the current ring position, remove it from
            // the head of that symbol's occurrence list
            int oldValue = ring[ringPos] & 0xFF;
            short oldHead = heads[oldValue];

            if (oldHead != NULL)
            {
                short newHead = links[oldHead];

                if (newHead == NULL)
                {
                    tails[oldValue] = NULL;
                }

                heads[oldValue] = newHead;
            }

            // Insert new symbol (at the current ring position) at the tail of
            // the new symbol's occurrence list
            short oldTail = tails[newValue];
            short newTail = (short)ringPos;

            if (oldTail == NULL)
            {
                heads[newValue] = newTail;
            }
            else
            {
                links[oldTail] = newTail;
            }

            tails[newValue] = newTail;
            links[newTail] = NULL;

            // Finally, the easy part: update the ring and advance our position.
            ring[ringPos] = (byte)newValue;
            ringPos = (ringPos + 1) & 0xFFF;
        }

        private void initCursors(int b)
        {
            ncursors = 0;

            for (short pos = heads[b]; pos != NULL; pos = links[pos])
            {
                /*
                 * This SHOULD (and in fact DOES) work but Konami made
                 * distance=0 mean End of File for some bone head fucking
                 * reason. This is also why close() is nontrivial (because you
                 * NEED to send an EOF marker or their decompressor throws a
                 * shit fit).
                 *
                 * Basically the stuff inside the 'if' should always work but
                 * we have to filter this particular case out.
                 */
                if (pos != (ringPos & 0xFFF))
                {
                    cursors[ncursors++] = pos;
                }
            }

            if (ncursors > 0)
            {
                match[nmatched++] = (byte)b;
            }
            else
            {
                pushVerbatim((byte)b);
            }
        }

        private void updateCursors(int b)
        {
            int cursorNo = 0;

            while (cursorNo < ncursors)
            {
                int pos = (cursors[cursorNo] + nmatched) & 0xFFF;

                if ((ring[pos] & 0xFF) != b)
                {
                    if (ncursors > 1)
                    {
                        // Kill this cursor and stay put (so we process whatever
                        // cursor has moved into the evicted cursor's slot in the
                        // next iteration)
                        cursors[cursorNo] = cursors[--ncursors];
                    }
                    else
                    {
                        // Last remaining match, emit this and restart with new symbol
                        // That restart is a subtle point that took me a while to remember! :)
                        emitMatch();
                        initCursors(b);

                        return;
                    }
                }
                else
                {
                    // Move to next cursor slot
                    cursorNo++;
                }
            }

            match[nmatched++] = (byte)b;
        }


        private void pushVerbatim(byte b)
        {
            packet[pktPos++] = b;
            flags = (flags >> 1) | 0x80;

            if ((flags & 0x100) != 0)
            {
                writePacket();
            }
        }

        private void emitMatch()
        {
            if (nmatched < MIN_MATCH)
            {
                // Below break-even point, emit verbatim
                for (int i = 0; i < nmatched; i++)
                {
                    pushVerbatim(match[i]);
                }
            }
            else
            {
                // Above break-even, emit backref.
                // Note that at this stage, either only one match remains, or all
                // matches exceed the max match length (so we may as well use the
                // cursor in the first slot anyway)
                flags = (flags >> 1);

                int matchStartPos = (ringPos - nmatched) & 0xFFF;
                int matchOffset = (matchStartPos - cursors[0]) & 0xFFF;

                packet[pktPos++] = (byte)(matchOffset >> 4);
                packet[pktPos++] = (byte)(((matchOffset & 0x0F) << 4) | (nmatched - MIN_MATCH));

                if ((flags & 0x100) != 0)
                {
                    writePacket();
                }
            }

            nmatched = 0;
        }
        private void writePacket()
        {

            output.WriteByte((byte)(flags & 0xFF));
            output.Write(packet, 0, pktPos);

            pktPos = 0;
            flags = 0x10000;
        }


    }

}


