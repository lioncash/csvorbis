/* csvorbis
 * Copyright (C) 2000 ymnk, JCraft,Inc.
 *  
 * Written by: 2000 ymnk<ymnk@jcraft.com>
 * Ported to C# from JOrbis by: Mark Crichton <crichton@gimp.org> 
 *   
 * Thanks go to the JOrbis team, for licencing the code under the
 * LGPL, making my job a lot easier.
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Library General Public License
 * as published by the Free Software Foundation; either version 2 of
 * the License, or (at your option) any later version.
   
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Library General Public License for more details.
 * 
 * You should have received a copy of the GNU Library General Public
 * License along with this program; if not, write to the Free Software
 * Foundation, Inc., 675 Mass Ave, Cambridge, MA 02139, USA.
 */

// Alteration of FileShare mode on line 97 by Matthew Harris for use as part of the DragonOgg library. Remove the following to revert:
// , FileShare.ReadWrite
// Be aware that this does introduce potential difficulties with the file being written between writing operations, but is needed to allow
// the DragonOgg library to work properly w.r.t writing tags.
// Also added IDisposable implementation to properly close down the filestream


using System;
using System.IO;
using csogg;

namespace csvorbis 
{
	/// <summary>
	/// Represents an OGG Vorbis file.
	/// </summary>
	public class VorbisFile : IDisposable
	{
		private const int CHUNKSIZE = 8500;
		private const int SEEK_SET = 0;
		private const int SEEK_CUR = 1;
		private const int SEEK_END = 2;

		private const int OV_FALSE = -1;
		private const int OV_EOF = -2;
		private const int OV_HOLE = -3;

		internal const int OV_EREAD = -128;
		internal const int OV_EFAULT = -129;
		internal const int OV_EIMPL = -130;
		internal const int OV_EINVAL = -131;
		internal const int OV_ENOTVORBIS = -132;
		internal const int OV_EBADHEADER = -133;
		internal const int OV_EVERSION = -134;
		internal const int OV_ENOTAUDIO = -135;
		internal const int OV_EBADPACKET = -136;
		internal const int OV_EBADLINK = -137;
		internal const int OV_ENOSEEK = -138;

		private FileStream datasource;
		private bool skable = false;
		private long offset;
		// long end;

		private SyncState oy = new SyncState();

		private int links;
		private long[] offsets;
		private long[] dataoffsets;
		private int[] serialnos;
		private long[] pcmlengths;
		private Info[] vi;
		private Comment[] vc;

		// Decoding working state local storage
		private long pcm_offset;
		private bool decode_ready = false;
		private int current_serialno;
		private int current_link;

		private float bittrack;
		private float samptrack;

		private readonly StreamState os;
		private readonly DspState vd;
		private readonly Block vb;

		private VorbisFile()
		{
			os = new StreamState(); // take physical pages, weld into a logical
			// stream of packets
			vd = new DspState(); // central working state for 
			// the packet->PCM decoder
			vb = new Block(vd);     // local working space for packet->PCM decode
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="file">Path to the OGG Vorbis file to encapsulate.</param>
		public VorbisFile(String file) : this()
		{
			FileStream inst;

			try
			{
				inst = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			}
			catch (IOException e)
			{
				throw new csorbisException("VorbisFile: " + e.Message);
			}

			int ret = open(inst, null, 0);
			if (ret == -1)
			{
				throw new csorbisException("VorbisFile: open return -1");
			}
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="inst">FileStream instance containing a handle to the OGG Vorbis data.</param>
		/// <param name="initial">Initial bytes to read.</param>
		/// <param name="ibytes">Number of total bytes to read.</param>
		public VorbisFile(FileStream inst, byte[] initial, int ibytes) : this()
		{
			open(inst, initial, ibytes);
		}

		private int get_data()
		{
			int index = oy.buffer(CHUNKSIZE);
			byte[] buffer = oy.data;
			//  int bytes=callbacks.read_func(buffer, index, 1, CHUNKSIZE, datasource);
			int bytes = 0;
			try
			{
				bytes = datasource.Read(buffer, index, CHUNKSIZE);
			}
			catch (Exception e)
			{
				Console.Error.WriteLine(e.Message);
				return OV_EREAD;
			}

			oy.wrote(bytes);
			if (bytes == -1)
			{
				bytes = 0;
			}
			return bytes;
		}

		private void seek_helper(long offst)
		{
			//callbacks.seek_func(datasource, offst, SEEK_SET);
			fseek(datasource, offst, SEEK_SET);
			this.offset = offst;
			oy.reset();
		}

		private int get_next_page(Page page, long boundary)
		{
			if (boundary > 0) boundary += offset;
			while (true)
			{
				if (boundary > 0 && offset >= boundary)
					return OV_FALSE;

				int more = oy.pageseek(page);
				if (more < 0)
				{
					offset -= more;
				}
				else
				{
					if (more == 0)
					{
						if (boundary == 0)
							return OV_FALSE;

						//  if(get_data()<=0)return -1;
						int ret = get_data();
						if (ret == 0) return OV_EOF;
						if (ret < 0) return OV_EREAD;
					}
					else
					{
						int ret = (int) offset; //!!!
						offset += more;
						return ret;
					}
				}
			}
		}

		private int get_prev_page(Page page)
		{
			long begin = offset; //!!!
			int ret;
			int offst = - 1;
			while (offst == -1)
			{
				begin -= CHUNKSIZE;
				if (begin < 0)
					begin = 0;
				seek_helper(begin);

				while (offset < begin + CHUNKSIZE)
				{
					ret = get_next_page(page, begin + CHUNKSIZE - offset);
					if (ret == OV_EREAD)
					{
						return OV_EREAD;
					}

					if (ret < 0)
					{
						break;
					}
					else
					{
						offst = ret;
					}
				}
			}

			seek_helper(offst); //!!!
			ret = get_next_page(page, CHUNKSIZE);
			if (ret < 0)
			{
				//System.err.println("Missed page fencepost at end of logical bitstream Exiting");
				//System.exit(1);
				return OV_EFAULT;
			}

			return offst;
		}

		private int bisect_forward_serialno(long begin, long searched, long end, int currentno, int m)
		{
			long endsearched = end;
			long next = end;
			Page page = new Page();
			int ret;

			while (searched < endsearched)
			{
				long bisect;
				if (endsearched - searched < CHUNKSIZE)
				{
					bisect = searched;
				}
				else
				{
					bisect = (searched + endsearched)/2;
				}

				seek_helper(bisect);
				ret = get_next_page(page, -1);
				if (ret == OV_EREAD)
					return OV_EREAD;

				if (ret < 0 || page.serialno() != currentno)
				{
					endsearched = bisect;
					if (ret >= 0)
						next = ret;
				}
				else
				{
					searched = ret + page.header_len + page.body_len;
				}
			}
			seek_helper(next);
			ret = get_next_page(page, -1);
			if (ret == OV_EREAD)
				return OV_EREAD;

			if (searched >= end || ret == -1)
			{
				links = m + 1;
				offsets = new long[m + 2];
				offsets[m + 1] = searched;
			}
			else
			{
				ret = bisect_forward_serialno(next, offset, end, page.serialno(), m + 1);

				if (ret == OV_EREAD)
					return OV_EREAD;
			}
			offsets[m] = begin;
			return 0;
		}

		// uses the local ogg_stream storage in vf; this is important for
		// non-streaming input sources
		private int fetch_headers(Info vi, Comment vc, int[] serialno, Page og_ptr)
		{
			Page og = new Page();
			Packet op = new Packet();
			int ret;

			if (og_ptr == null)
			{
				ret = get_next_page(og, CHUNKSIZE);

				if (ret == OV_EREAD)
					return OV_EREAD;
				if (ret < 0)
					return OV_ENOTVORBIS;

				og_ptr = og;
			}

			if (serialno != null)
				serialno[0] = og_ptr.serialno();

			os.init(og_ptr.serialno());

			// extract the initial header from the first page and verify that the
			// Ogg bitstream is in fact Vorbis data

			vi.init();
			vc.init();

			int i = 0;
			while (i < 3)
			{
				os.pagein(og_ptr);
				while (i < 3)
				{
					int result = os.packetout(op);
					if (result == 0)
						break;

					if (result == -1)
					{
						Console.Error.WriteLine("Corrupt header in logical bitstream.");
						//goto bail_header;
						vi.clear();
						vc.clear();
						os.clear();
						return -1;
					}

					if (vi.synthesis_headerin(vc, op) != 0)
					{
						Console.Error.WriteLine("Illegal header in logical bitstream.");
						//goto bail_header;
						vi.clear();
						vc.clear();
						os.clear();
						return -1;
					}
					i++;
				}

				if (i < 3)
				{
					if (get_next_page(og_ptr, 1) < 0)
					{
						Console.Error.WriteLine("Missing header in logical bitstream.");
						//goto bail_header;
						vi.clear();
						vc.clear();
						os.clear();
						return -1;
					}
				}
			}
			return 0;
		}

		// last step of the OggVorbis_File initialization; get all the
		// vorbis_info structs and PCM positions.  Only called by the seekable
		// initialization (local stream storage is hacked slightly; pay
		// attention to how that's done)
		private void prefetch_all_headers(Info first_i, Comment first_c, int dataoffset)
		{
			Page og = new Page();

			vi = new Info[links];
			vc = new Comment[links];
			dataoffsets = new long[links];
			pcmlengths = new long[links];
			serialnos = new int[links];

			for (int i=0;i<links;i++)
			{
				if (first_i!=null && first_c!=null && i==0)
				{
					// we already grabbed the initial header earlier.  This just
					// saves the waste of grabbing it again
					vi[i]=first_i;
					vc[i]=first_c;
					dataoffsets[i]=dataoffset;
				}
				else
				{
					// seek to the location of the initial header
					seek_helper(offsets[i]); //!!!
					if (fetch_headers(vi[i], vc[i], null, null) == -1)
					{
						Console.Error.WriteLine("Error opening logical bitstream #" + (i + 1) + "\n");
						dataoffsets[i] = -1;
					}
					else
					{
						dataoffsets[i] = offset;
						os.clear();
					}
				}

				// get the serial number and PCM length of this link. To do this,
				// get the last page of the stream
				long end = offsets[i + 1]; //!!!
				seek_helper(end);

				while(true)
				{
					int ret = get_prev_page(og);
					if (ret == -1)
					{
						// this should not be possible
						Console.Error.WriteLine("Could not find last page of logical "+
							"bitstream #"+(i)+"\n");
						vi[i].clear();
						vc[i].clear();
						break;
					}

					if (og.granulepos()!=-1)
					{
						serialnos[i] = og.serialno();
						pcmlengths[i] = og.granulepos();
						break;
					}
				}
			}
		}

		private int make_decode_ready()
		{
#if NET_2_1
			if(decode_ready)
				throw new Exception ("make_decode_ready: 1");
#else
			if (decode_ready)
				Environment.Exit(1);
#endif
			vd.synthesis_init(vi[0]);
			vb.init(vd);
			decode_ready = true;
			return 0;
		}

		private int open_seekable()
		{
			Info initial_i = new Info();
			Comment initial_c = new Comment();
			Page og = new Page();
			// is this even vorbis...?
			int[] foo = new int[1];
			int ret = fetch_headers(initial_i, initial_c, foo, null);
			int serialno = foo[0];
			int dataoffset = (int) offset; //!!
			os.clear();

			if(ret == -1)
				return -1;

			// we can seek, so set out learning all about this file
			skable = true;
			//(callbacks.seek_func)(datasource, 0, SEEK_END);
			fseek(datasource, 0, SEEK_END);
			//offset=end=(callbacks.tell_func)(datasource);
			offset = ftell(datasource);

			// We get the offset for the last page of the physical bitstream.
			// Most OggVorbis files will contain a single logical bitstream
			long end = get_prev_page(og);
			// more than one logical bitstream?
			if (og.serialno()!=serialno)
			{
				// Chained bitstream. Bisect-search each logical bitstream
				// section.  Do so based on serial number only
				if (bisect_forward_serialno(0,0,end+1,serialno,0) < 0)
				{
					clear();
					return OV_EREAD;
				}
			}
			else
			{
				// Only one logical bitstream
				if (bisect_forward_serialno(0,end,end+1,serialno,0) < 0)
				{
					clear();
					return OV_EREAD;
				}
			}
			prefetch_all_headers(initial_i, initial_c, dataoffset);
			return raw_seek(0);
		}

		private int open_nonseekable()
		{
			//System.err.println("open_nonseekable");
			// we cannot seek. Set up a 'single' (current) logical bitstream entry
			links=1;
			vi=new Info[links]; vi[0]=new Info(); // ??
			vc=new Comment[links]; vc[0]=new Comment(); // ?? bug?

			// Try to fetch the headers, maintaining all the storage
			int[] foo = new int[1];
			if (fetch_headers(vi[0], vc[0], foo, null) == -1)
				return -1;
			current_serialno = foo[0];
			make_decode_ready();
			return 0;
		}

		// clear out the current logical bitstream decoder
		private void decode_clear()
		{
			os.clear();
			vd.clear();
			vb.clear();
			decode_ready = false;
			bittrack = 0.0f;
			samptrack = 0.0f;
		}

		// fetch and process a packet.  Handles the case where we're at a
		// bitstream boundary and dumps the decoding machine.  If the decoding
		// machine is unloaded, it loads it.  It also keeps pcm_offset up to
		// date (seek and read both use this.  seek uses a special hack with
		// readp). 
		//
		// return: -1) hole in the data (lost packet) 
		//          0) need more date (only if readp==0)/eof
		//          1) got a packet 

		private int process_packet(int readp)
		{
			Page og = new Page();

			// handle one packet.  Try to fetch it from current stream state
			// extract packets from page
			while (true)
			{
				// process a packet if we can.  If the machine isn't loaded,
				// neither is a page
				if (decode_ready)
				{
					Packet op = new Packet();
					int result = os.packetout(op);

					// if(result==-1)return(-1); // hole in the data. For now, swallow
					// and go. We'll need to add a real
					// error code in a bit.
					if (result > 0)
					{
						// got a packet.  process it
						long granulepos = op.granulepos;
						if (vb.synthesis(op) == 0)
						{ // lazy check for lazy
							// header handling.  The
							// header packets aren't
							// audio, so if/when we
							// submit them,
							// vorbis_synthesis will
							// reject them
							// suck in the synthesis data and track bitrate
						{
							int oldsamples = vd.synthesis_pcmout(null, null);
							vd.synthesis_blockin(vb);
							samptrack+=vd.synthesis_pcmout(null, null)-oldsamples;
							bittrack+=op.bytes*8;
						}

							// update the pcm offset.
							if(granulepos!=-1 && op.e_o_s==0)
							{
								int link=(skable?current_link:0);

								// this packet has a pcm_offset on it (the last packet
								// completed on a page carries the offset) After processing
								// (above), we know the pcm position of the *last* sample
								// ready to be returned. Find the offset of the *first*
								// 
								// As an aside, this trick is inaccurate if we begin
								// reading anew right at the last page; the end-of-stream
								// granulepos declares the last frame in the stream, and the
								// last packet of the last page may be a partial frame.
								// So, we need a previous granulepos from an in-sequence page
								// to have a reference point.  Thus the !op.e_o_s clause above

								int samples=vd.synthesis_pcmout(null, null);
								granulepos-=samples;
								for (int i=0;i<link;i++)
								{
									granulepos+=pcmlengths[i];
								}
								pcm_offset=granulepos;
							}
							return 1;
						}
					}
				}

				if(readp == 0)
					return 0;

				// If EOF leave uninitialized.
				if(get_next_page(og,-1) < 0)
					return 0;

				// bitrate tracking; add the header's bytes here, the body bytes
				// are done by packet above
				bittrack+=og.header_len*8;

				// has our decoding just traversed a bitstream boundary?
				if(decode_ready)
				{
					if(current_serialno!=og.serialno())
					{
						decode_clear();
					}
				}

				// Do we need to load a new machine before submitting the page?
				// This is different in the seekable and non-seekable cases.  
				// 
				// In the seekable case, we already have all the header
				// information loaded and cached; we just initialize the machine
				// with it and continue on our merry way.
				// 
				// In the non-seekable (streaming) case, we'll only be at a
				// boundary if we just left the previous logical bitstream and
				// we're now nominally at the header of the next bitstream

				if(!decode_ready)
				{
					if (skable)
					{
						current_serialno = og.serialno();

						// match the serialno to bitstream section.  We use this rather than
						// offset positions to avoid problems near logical bitstream
						// boundaries
						int i;
						for (i = 0; i < links; i++)
						{
							if (serialnos[i] == current_serialno)
								break;
						}

						// sign of a bogus stream.  error out,
						if (i == links)
							return -1;

						// leave machine uninitialized
						current_link = i;

						os.init(current_serialno);
						os.reset();

					}
					else
					{
						// we're streaming
						// fetch the three header packets, build the info struct
						int[] foo = new int[1];
						int ret = fetch_headers(vi[0], vc[0], foo, og);
						current_serialno = foo[0];
						if (ret != 0)
							return ret;
						current_link++;
					}
					make_decode_ready();
				}
				os.pagein(og);
			}
		}

		//The helpers are over; it's all toplevel interface from here on out
		// clear out the OggVorbis_File struct
		private void clear()
		{
			vb.clear();
			vd.clear();
			os.clear();

			if (vi != null && links != 0)
			{
				for (int i = 0; i < links; i++)
				{
					vi[i].clear();
					vc[i].clear();
				}
				vi = null;
				vc = null;
			}

			dataoffsets = null;
			pcmlengths = null;
			serialnos = null;
			offsets = null;
			oy.clear();
		}

		private static int fseek(FileStream fis, long off, int whence)
		{
			if (fis.CanSeek)
			{
				try
				{
					if (whence == SEEK_SET)
					{
						fis.Seek(off, 0);
					}
					else if (whence == SEEK_END)
					{
						fis.Seek(fis.Length - off, 0);
					}
					else
					{
						Console.Error.WriteLine("seek: " + whence + " is not supported");
					}
				}
				catch (Exception e)
				{
					Console.Error.WriteLine(e.Message);
				}

				return 0;
			}

			try
			{
				if (whence == 0)
				{
					fis.Seek(0, 0);
				}

				fis.Seek(off, 0);
			}
			catch (Exception e)
			{
				Console.Error.WriteLine(e.Message);
				return -1;
			}

			return 0;
		}

		private static long ftell(FileStream fis)
		{
			try
			{
				if (fis.CanSeek)
				{
					return (fis.Position);
				}
			}
			catch (Exception e)
			{
				Console.Error.WriteLine(e.Message);
			}

			return 0;
		}

		// inspects the OggVorbis file and finds/documents all the logical
		// bitstreams contained in it.  Tries to be tolerant of logical
		// bitstream sections that are truncated/woogie. 
		//
		// return: -1) error
		//          0) OK

		private int open(FileStream iis, byte[] initial, int ibytes)
		{
			return open_callbacks(iis, initial, ibytes);
		}

		private int open_callbacks(FileStream iis, byte[] initial, int ibytes)
		{
			this.datasource = iis;

			// Initialize the framing state
			oy.init();

			// Perhaps some data was previously read into a buffer for testing
			// against other stream types.  Allow initialization from this
			// previously read data (as we may be reading from a non-seekable
			// stream)
			if (initial != null)
			{
				int index = oy.buffer(ibytes);
				Array.Copy(initial, 0, oy.data, index, ibytes);
				oy.wrote(ibytes);
			}

			// Can we seek? Stevens suggests the seek test was portable
			int ret;
			if (iis.CanSeek)
			{
				ret = open_seekable();
			}
			else
			{
				ret = open_nonseekable();
			}

			if (ret != 0)
			{
				datasource = null;
				clear();
			}

			return ret;
		}

		/// <summary>
		/// Gets the number of logical bitstreams
		/// within this physical bitstream.
		/// </summary>
		/// <returns>the number of logical bitstreams in this physical one.</returns>
		public int streams()
		{
			return links;
		}

		/// <summary>
		/// Whether or not this OGG Vorbis file is seekable.
		/// </summary>
		/// <returns>true if the OGG Vorbis file is seekable; false otherwise.</returns>
		public bool seekable()
		{
			return skable;
		}

		/// <summary>
		/// Gets the bitrate for a given logical bitstream or the entire
		/// physical bitstream.  If the file is open for random access, it will
		/// find the *actual* average bitrate.  If the file is streaming, it
		/// returns the nominal bitrate (if set) else the average of the
		/// upper/lower bounds (if set) else -1 (unset).
		/// 
		/// If you want the actual bitrate field settings, get them from the
		/// vorbis_info structs
		/// </summary>
		/// <param name="i">Bitstream index</param>
		/// <returns>Bitrate for the given bitstream, or the entire physical bitstream.</returns>
		public int bitrate(int i)
		{
			if (i >= links)
				return -1;

			if (!skable && i != 0)
				return bitrate(0);

			if (i < 0)
			{
				long bits = 0;
				for (int j = 0; j < links; j++)
				{
					bits += (offsets[j + 1] - dataoffsets[j])*8;
				}
				return ((int) Math.Round(bits/time_total(-1)));
			}
			else
			{
				if (skable)
				{
					// return the actual bitrate
					return ((int) Math.Round((offsets[i + 1] - dataoffsets[i])*8/time_total(i)));
				}
				else
				{
					// return nominal if set
					if (vi[i].bitrate_nominal > 0)
					{
						return vi[i].bitrate_nominal;
					}
					else
					{
						if (vi[i].bitrate_upper > 0)
						{
							if (vi[i].bitrate_lower > 0)
							{
								return (vi[i].bitrate_upper + vi[i].bitrate_lower)/2;
							}
							else
							{
								return vi[i].bitrate_upper;
							}
						}

						return -1;
					}
				}
			}
		}

		/// <summary>
		/// Gets the actual bitrate since last call.
		/// </summary>
		/// <returns>
		/// Acutal bitrate since last call.
		/// Returns -1 if there is no additional data to offer since the last call
		/// (or if at the beginning of a stream).
		/// </returns>
		public int bitrate_instant()
		{
			if (samptrack == 0)
				return -1;

			int _link = (skable ? current_link : 0);
			int ret = (int) (bittrack/samptrack*vi[_link].rate + .5);
			bittrack = 0.0f;
			samptrack = 0.0f;
			return ret;
		}

		public int serialnumber(int i)
		{
			if (i >= links)
				return -1;

			if (!skable && i >= 0)
				return serialnumber(-1);

			if (i < 0)
			{
				return current_serialno;
			}
			else
			{
				return serialnos[i];
			}
		}

		/// <summary>
		/// Gets total raw (compressed) length of content if i==-1.
		/// Raw (compressed) length of that logical bitstream for i==0 to n
		/// -1 if the stream is not seekable (we can't know the length)
		/// </summary>
		/// <param name="i">Bitstream Index</param>
		/// <returns>Compressed length. Returns -1 if stream is not seekable.</returns>
		public long raw_total(int i)
		{
			if (!skable || i >= links)
				return -1;

			if (i < 0)
			{
				long acc = 0; // bug?

				for (int j = 0; j < links; j++)
				{
					acc += raw_total(j);
				}

				return acc;
			}
			else
			{
				return (offsets[i + 1] - offsets[i]);
			}
		}

		/// <summary>
		/// Gets the total PCM length (samples) of content if i==-1
		//  PCM length (samples) of that logical bitstream for i==0 to n
		/// </summary>
		/// <param name="i">Bitstream index</param>
		/// <returns>Total PCM length. Returns -1 if stream is not seekable.</returns>
		public long pcm_total(int i)
		{
			if (!skable || i >= links)
				return -1;

			if (i < 0)
			{
				long acc = 0;

				for (int j = 0; j < links; j++)
				{
					acc += pcm_total(j);
				}

				return acc;
			}
			else
			{
				return pcmlengths[i];
			}
		}

		/// <summary>
		/// Total seconds of content if i==-1
		/// seconds in that logical bitstream for i==0 to n
		/// </summary>
		/// <param name="i">Bitstream index.</param>
		/// <returns>Total seconds of current overall file or for a specific bitstream.</returns>
		public float time_total(int i)
		{
			if (!skable || i >= links)
				return -1;

			if (i < 0)
			{
				float acc = 0;

				for (int j = 0; j < links; j++)
				{
					acc += time_total(j);
				}

				return acc;
			}
			else
			{
				return ((float) (pcmlengths[i])/vi[i].rate);
			}
		}

		/// <summary>
		/// Seek to an offset relative to the *compressed* data. This also
		/// immediately sucks in and decodes pages to update the PCM cursor. It
		/// will cross a logical bitstream boundary, but only if it can't get
		/// any packets out of the tail of the bitstream we seek to (so no
		/// surprises). 
		/// </summary>
		/// <param name="pos">Byte offset to seek to.</param>
		/// <returns>Zero upon success; non-zero upon failure.</returns>
		public int raw_seek(int pos)
		{
			// don't dump machine if we can't seek
			if (!skable)
				return -1;

			if (pos < 0 || pos > offsets[links])
			{
				//goto seek_error;
				pcm_offset = -1;
				decode_clear();
				return -1;
			}

			// clear out decoding machine state
			pcm_offset = -1;
			decode_clear();

			// seek
			seek_helper(pos);

			// we need to make sure the pcm_offset is set.  We use the
			// _fetch_packet helper to process one packet with readp set, then
			// call it until it returns '0' with readp not set (the last packet
			// from a page has the 'granulepos' field set, and that's how the
			// helper updates the offset

			switch (process_packet(1))
			{
				case 0:
					// oh, eof. There are no packets remaining.  Set the pcm offset to
					// the end of file
					pcm_offset = pcm_total(-1);
					return 0;

				case -1:
					// error! missing data or invalid bitstream structure
					//goto seek_error;
					pcm_offset = -1;
					decode_clear();
					return -1;

				default:
					// all OK
					break;
			}
			while (true)
			{
				switch (process_packet(0))
				{
					case 0:
						// the offset is set.  If it's a bogus bitstream with no offset
						// information, it's not but that's not our fault.  We still run
						// gracefully, we're just missing the offset
						return 0;

					case -1:
						// error! missing data or invalid bitstream structure
						//goto seek_error;
						pcm_offset = -1;
						decode_clear();
						return -1;

					default:
						// continue processing packets
						break;
				}
			}
			// seek_error:
			// dump the machine so we're in a known state
			//pcm_offset=-1;
			//decode_clear();
			//return -1;
		}

		/// <summary>
		/// Seek to a sample offset relative to the decompressed pcm stream 
		/// </summary>
		/// <param name="pos">Sample offset to seek to.</param>
		/// <returns>Zero upon success; Non-zero upon failure.</returns>
		public int pcm_seek(long pos)
		{
			int link = -1;
			long total = pcm_total(-1);

			if (!skable) return (-1); // don't dump machine if we can't seek
			if (pos < 0 || pos > total)
			{
				//goto seek_error;
				pcm_offset = -1;
				decode_clear();
				return -1;
			}

			// which bitstream section does this pcm offset occur in?
			for (link = links - 1; link >= 0; link--)
			{
				total -= pcmlengths[link];
				if (pos >= total) break;
			}

			// search within the logical bitstream for the page with the highest
			// pcm_pos preceeding (or equal to) pos.  There is a danger here;
			// missing pages or incorrect frame number information in the
			// bitstream could make our task impossible.  Account for that (it
			// would be an error condition)
			{
				long target = pos - total;
				long end = offsets[link + 1];
				long begin = offsets[link];
				int best = (int) begin;

				Page og = new Page();
				while (begin < end)
				{
					long bisect;

					if (end - begin < CHUNKSIZE)
					{
						bisect = begin;
					}
					else
					{
						bisect = (end + begin)/2;
					}

					seek_helper(bisect);
					int ret = get_next_page(og, end - bisect);

					if (ret == -1)
					{
						end = bisect;
					}
					else
					{
						long granulepos = og.granulepos();
						if (granulepos < target)
						{
							best = ret; // raw offset of packet with granulepos
							begin = offset; // raw offset of next packet
						}
						else
						{
							end = bisect;
						}
					}
				}
				// found our page. seek to it (call raw_seek).
				if (raw_seek(best) != 0)
				{
					//goto seek_error;
					pcm_offset = -1;
					decode_clear();
					return -1;
				}
			}

			// verify result
			if (pcm_offset >= pos)
			{
				//goto seek_error;
				pcm_offset = -1;
				decode_clear();
				return -1;
			}
			if (pos > pcm_total(-1))
			{
				//goto seek_error;
				pcm_offset = -1;
				decode_clear();
				return -1;
			}

			// discard samples until we reach the desired position. Crossing a
			// logical bitstream boundary with abandon is OK.
			while (pcm_offset < pos)
			{
				int target = (int) (pos - pcm_offset);
				float[][][] pcm = new float[1][][];
				int[] index = new int[getInfo(-1).channels];
				int samples = vd.synthesis_pcmout(pcm, index);

				if (samples > target)
					samples = target;

				vd.synthesis_read(samples);
				pcm_offset += samples;

				if (samples < target)
				{
					if (process_packet(1) == 0)
					{
						pcm_offset = pcm_total(-1); // eof
					}
				}
			}

			return 0;

			// seek_error:
			// dump machine so we're in a known state
			//pcm_offset=-1;
			//decode_clear();
			//return -1;
		}

		/// <summary>
		/// Seek to a playback time relative to the decompressed PCM stream.
		/// returns zero on success, nonzero on failure
		/// </summary>
		/// <param name="seconds">Number of seconds to seek relative to the current position.</param>
		/// <returns>Zero upon success; Non-zero upon failure.</returns>
		public int time_seek(float seconds)
		{
			// translate time to PCM position and call pcm_seek

			int link = -1;
			long pcm_tot = pcm_total(-1);
			float time_tot = time_total(-1);

			// don't dump machine if we can't seek
			if (!skable)
				return -1;

			if (seconds < 0 || seconds > time_tot)
			{
				//goto seek_error;
				pcm_offset = -1;
				decode_clear();
				return -1;
			}

			// which bitstream section does this time offset occur in?
			for (link = links - 1; link >= 0; link--)
			{
				pcm_tot -= pcmlengths[link];
				time_tot -= time_total(link);

				if (seconds >= time_tot)
					break;
			}

			// enough information to convert time offset to pcm offset
			{
				long target = (long) (pcm_tot + (seconds - time_tot)*vi[link].rate);
				return pcm_seek(target);
			}
		}

		/// <summary>
		/// Gets the current stream offset.
		/// Note that seek followed by tell will likely not give the set offset due to caching.
		/// </summary>
		/// <returns>The current stream offset.</returns>
		public long raw_tell()
		{
			return offset;
		}

		/// <summary>
		/// Gets the PCM offset (sample) of the next PCM sample to be read
		/// </summary>
		/// <returns>the PCM offset (sample) of the next PCM sample to be read</returns>
		public long pcm_tell()
		{
			return pcm_offset;
		}

		/// <summary>
		/// Gets the time offset (in seconds) of the next PCM sample to be read.
		/// </summary>
		/// <returns>the time offset (in seconds) of the next PCM sample to be read.</returns>
		public float time_tell()
		{
			// translate time to PCM position and call pcm_seek

			int link = -1;
			long pcm_tot = 0;
			float time_tot = 0.0f;

			if (skable)
			{
				pcm_tot = pcm_total(-1);
				time_tot = time_total(-1);

				// which bitstream section does this time offset occur in?
				for (link = links - 1; link >= 0; link--)
				{
					pcm_tot -= pcmlengths[link];
					time_tot -= time_total(link);

					if (pcm_offset >= pcm_tot)
						break;
				}
			}

			return ((float) time_tot + (float) (pcm_offset - pcm_tot)/vi[link].rate);
		}

		/// <summary>
		/// Requests information about a specified bitstream section.
		/// </summary>
		/// 
		/// <param name="link">
		/// The bitstream link. Pass -1 to get the Info
		/// object of the current bitstream section being decoded.
		/// Pass 0-n to get info about that specific bitstream sequence.
		/// </param>
		/// 
		/// <returns>Information about a bitstream sequence.</returns>
		public Info getInfo(int link)
		{
			if (skable)
			{
				if (link < 0)
				{
					if (decode_ready)
						return vi[current_link];
					else
						return null;
				}
				else
				{
					if (link >= links)
						return null;
					else
						return vi[link];
				}
			}
			else
			{
				if (decode_ready)
					return vi[0];
				else
					return null;
			}
		}

		/// <summary>
		/// Gets a specfic Vorbis comment based on its index in the overall comments.
		/// </summary>
		/// <param name="link">The vorbis comment index.</param>
		/// <returns>A Vorbis comment based upon the given index.</returns>
		public Comment getComment(int link)
		{
			if (skable)
			{
				if (link < 0)
				{
					if (decode_ready)
						return vc[current_link];
					else
						return null;
				}
				else
				{
					if (link >= links)
						return null;
					else
						return vc[link];
				}
			}
			else
			{
				if (decode_ready)
					return vc[0];
				else
					return null;
			}
		}

		// Up to this point, everything could more or less hide the multiple
		// logical bitstream nature of chaining from the toplevel application
		// if the toplevel application didn't particularly care.  However, at
		// the point that we actually read audio back, the multiple-section
		// nature must surface: Multiple bitstream sections do not necessarily
		// have to have the same number of channels or sampling rate.

		/// <summary>
		/// read returns the sequential logical bitstream number currently
		/// being decoded along with the PCM data in order that the toplevel
		/// application can take action on channel/sample rate changes.  This
		/// number will be incremented even for streamed (non-seekable) streams
		/// (for seekable streams, it represents the actual logical bitstream
		/// index within the physical bitstream.  Note that the accessor
		/// functions above are aware of this dichotomy).
		/// </summary>
		/// <param name="buffer">Buffer that will hold the returned PCM data.</param>
		/// <param name="length">Number of bytes to place into the PCM buffer.</param>
		/// <param name="useBigEndian">Whether or not to use big-endian representation.</param>
		/// <param name="word">Word size, can be either 1 (byte) or 2 (16-bit short)</param>
		/// <param name="signed">Whether or not the stream is signed.</param>
		/// <param name="bitstream"></param>
		/// <returns>
		/// -1 - if an error/hole in data has occurred.
		///  0 - if end of file has been reached.
		///  N - number of PCM bytes actually returned.
		/// </returns>
		public int read(byte[] buffer, int length, bool useBigEndian, int word, bool signed, int[] bitstream)
		{
			// Test if the computer arch running this is big endian.
			bool hostIsBigEndian = !BitConverter.IsLittleEndian;
			int index = 0;

			while (true)
			{
				if (decode_ready)
				{
					float[][][] _pcm = new float[1][][];
					int[] _index = new int[getInfo(-1).channels];
					int samples = vd.synthesis_pcmout(_pcm, _index);
					float[][] pcm = _pcm[0];
					if (samples != 0)
					{
						// yay! proceed to pack data into the byte buffer
						int channels = getInfo(-1).channels;
						int bytespersample = word*channels;
						if (samples > length/bytespersample) samples = length/bytespersample;

						// a tight loop to pack each size
						{
							if (word == 1)
							{
								int off = (signed ? 0 : 128);
								for (int j = 0; j < samples; j++)
								{
									for (int i = 0; i < channels; i++)
									{
										int val = (int) (pcm[i][_index[i] + j]*128.0 + 0.5);

										if (val > 127)
											val = 127;
										else if (val < -128)
											val = -128;

										buffer[index++] = (byte) (val + off);
									}
								}
							}
							else
							{
								int off = (signed ? 0 : 32768);

								if (hostIsBigEndian == useBigEndian)
								{
									if (signed)
									{
										for (int i = 0; i < channels; i++)
										{
											// It's faster in this order
											int src = _index[i];
											int dest = i*2;
											for (int j = 0; j < samples; j++)
											{
												int val = (int) (pcm[i][src + j]*32767.0);

												if (val > 32767)
													val = 32767;
												else if (val < -32768)
													val = -32768;

												buffer[dest] = (byte) (val);
												buffer[dest + 1] = (byte) ((uint) val >> 8);
												dest += bytespersample;
											}
										}
									}
									else
									{
										for (int i = 0; i < channels; i++)
										{
											float[] src = pcm[i];
											int dest = i;
											for (int j = 0; j < samples; j++)
											{
												int val = (int) (src[j]*32768.0 + 0.5);

												if (val > 32767)
													val = 32767;
												else if (val < -32768)
													val = -32768;

												buffer[dest] = (byte) ((uint) (val + off) >> 8);
												buffer[dest + 1] = (byte) (val + off);
												dest += channels*2;
											}
										}
									}
								}
								else if (!useBigEndian)
								{
									for (int j = 0; j < samples; j++)
									{
										for (int i = 0; i < channels; i++)
										{
											int val = (int) (pcm[i][j]*32768.0 + 0.5);

											if (val > 32767)
												val = 32767;
											else if (val < -32768)
												val = -32768;

											val += off;
											buffer[index++] = (byte) ((uint) val >> 8);
											buffer[index++] = (byte) val;
										}
									}
								}
								else
								{
									for (int j = 0; j < samples; j++)
									{
										for (int i = 0; i < channels; i++)
										{
											int val = (int) (pcm[i][j]*32768.0 + 0.5);

											if (val > 32767)
												val = 32767;
											else if (val < -32768)
												val = -32768;

											val += off;
											buffer[index++] = (byte) val;
											buffer[index++] = (byte) ((uint) val >> 8);
										}
									}
								}
							}
						}

						vd.synthesis_read(samples);
						pcm_offset += samples;

						if (bitstream != null)
							bitstream[0] = current_link;

						return (samples*bytespersample);
					}
				}

				// suck in another packet
				switch (process_packet(1))
				{
					case 0:
						return 0;

					case -1:
						return -1;
				}
			}
		}

		/// <summary>
		/// Gets the Vorbis info within this file.
		/// </summary>
		/// <returns>the Vorbis info within this file.</returns>
		public Info[] getInfo()
		{
			return vi;
		}

		/// <summary>
		/// Gets the Vorbis comments within this file.
		/// </summary>
		/// <returns>the Vorbis comment within this file.</returns>
		public Comment[] getComments()
		{
			return vc;
		}
		
		#region IDisposable implementation
		public void Dispose ()
		{
			datasource.Close();
			datasource.Dispose();
			datasource = null;
		}
		
		#endregion
	}
}
