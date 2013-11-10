/* csogg
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

using System;
using System.Text;

namespace csogg
{
	/// <summary>
	/// Tracks the current encode/decode state of the current logical bitstream.
	/// </summary>
	public sealed class StreamState
	{
		private byte[] body_data;     /* Bytes from packet bodies */
		private int body_storage;     /* Storage elements allocated */
		private int body_fill;        /* Elements stored; fill mark */
		private  int body_returned;   /* Elements of fill returned */


		private int[] lacing_vals;    /* The values that will go to the segment table */
		private long[] granule_vals;  /* pcm_pos values for headers. Not compact
										 this way, but it is simple coupled to the
										 lacing fifo */

		private int lacing_storage;
		private int lacing_fill;
		private int lacing_packet;
		private int lacing_returned;

		private byte[] header = new byte[282];  /* Working space for header encode */
		private int header_fill;

		public int e_o_s;   /* Set when we have buffered the last packet in the logical bitstream */
		private int b_o_s;  /* Set after we've written the initial page of a logical bitstream */

		private int serialno;
		private int pageno;
		private long packetno; /* Sequence number for decode; the framing
								  knows where there's a hole in the data,
								  but we need coupling so that the codec
								  (which is in a seperate abstraction
								  layer) also knows about the gap */

		private long granulepos;

		/// <summary>
		/// Constructor
		/// </summary>
		public StreamState()
		{
			init();
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="serialno">Serial number</param>
		StreamState(int serialno) : this()
		{
			init(serialno);
		}

		private void init()
		{
			body_storage=16*1024;
			body_data=new byte[body_storage];
			lacing_storage=1024;
			lacing_vals=new int[lacing_storage];
			granule_vals=new long[lacing_storage];
		}

		/// <summary>
		/// Initializes a StreamState.
		/// </summary>
		/// <param name="serialno">Serial number to attach to this stream.</param>
		public void init(int serialno)
		{
			if (body_data == null)
			{
				init();
			}
			else
			{
				for (int i = 0; i < body_data.Length; i++)
					body_data[i] = 0;

				for (int i = 0; i < lacing_vals.Length; i++)
					lacing_vals[i] = 0;

				for (int i = 0; i < granule_vals.Length; i++)
					granule_vals[i] = 0;
			}

			this.serialno = serialno;
		}

		/// <summary>
		/// Clears this StreamState.
		/// </summary>
		public void clear()
		{
			body_data = null;
			lacing_vals = null;
			granule_vals = null;
		}

		/// <summary>
		/// 
		/// </summary>
		private void destroy()
		{
			clear();
		}

		private void body_expand(int needed)
		{
			if (body_storage<=body_fill+needed)
			{
				body_storage+=(needed+1024);
				byte[] foo=new byte[body_storage];
				Array.Copy(body_data, 0, foo, 0, body_data.Length);
				body_data=foo;
			}
		}

		private void lacing_expand(int needed)
		{
			if (lacing_storage<=lacing_fill+needed)
			{
				lacing_storage+=(needed+32);
				int[] foo=new int[lacing_storage];
				Array.Copy(lacing_vals, 0, foo, 0, lacing_vals.Length);
				lacing_vals=foo;

				long[] bar=new long[lacing_storage];
				Array.Copy(granule_vals, 0, bar, 0, granule_vals.Length);
				granule_vals=bar;
			}
		}

		/// <summary>
		/// Submits a packet to the bitstream for page encapsulation.
		/// </summary>
		/// After this is called, more packets can be submitted, or pages can be written out.
		/// <remarks>
		/// </remarks>
		/// <param name="op">Packet that is to be put into the bitstream.</param>
		/// <returns>
		/// 0 upon success.
		/// -1 upon error.
		/// </returns>
		public int packetin(Packet op)
		{
			int lacing_val=op.bytes/255+1;

			if (body_returned!=0)
			{
				// Advance packet data according to the body_returned pointer. We
				// had to keep it around to return a pointer into the buffer last call */

				body_fill-=body_returned;
				if (body_fill!=0)
				{
					Array.Copy(body_data, body_returned, body_data, 0, body_fill);
				}
				body_returned=0;
			}

			/* Make sure we have the buffer storage */
			body_expand(op.bytes);
			lacing_expand(lacing_val);

			// Copy in the submitted packet.  Yes, the copy is a waste; this is
			// the liability of overly clean abstraction for the time being.  It
			// will actually be fairly easy to eliminate the extra copy in the future

			Array.Copy(op.packet_base, op.packet, body_data, body_fill, op.bytes);
			body_fill+=op.bytes;

			/* Store lacing vals for this packet */
			int j;
			for (j=0;j<lacing_val-1;j++)
			{
				lacing_vals[lacing_fill+j]=255;
				granule_vals[lacing_fill+j]=granulepos;
			}
			lacing_vals[lacing_fill+j]=(op.bytes)%255;
			granulepos=granule_vals[lacing_fill+j]=op.granulepos;

			/* Flag the first segment as the beginning of the packet */
			lacing_vals[lacing_fill]|= 0x100;

			lacing_fill+=lacing_val;

			/* For the sake of completeness */
			packetno++;

			if (op.e_o_s != 0)
				e_o_s=1;

			return 0;
		}

		/// <summary>
		/// Assembles a data packet for output to the codec decoding engine
		/// </summary>
		/// <param name="op">Packet to be filled in with pointers to the new data.</param>
		/// <returns>
		/// -1 if we are out of sync and there is a gap in the data. This is usually a recoverable error and subsequent calls to ogg_stream_packetout are likely to succeed. op has not been updated.
		/// 0 if there is insufficient data available to complete a packet, or on unrecoverable internal error occurred. op has not been updated.
		/// 1 if a packet was assembled normally. op contains the next packet from the stream.
		/// </returns>
		public int packetout(Packet op)
		{

			// The last part of decode. We have the stream broken into packet
			// segments.  Now we need to group them into packets (or return the out of sync markers)

			int ptr=lacing_returned;

			if (lacing_packet<=ptr)
			{
				return 0;
			}

			if ((lacing_vals[ptr]&0x400)!=0)
			{
				/* We lost sync here; let the app know */
				lacing_returned++;

				/* We need to tell the codec there's a gap; it might need to handle previous packet dependencies. */
				packetno++;
				return -1;
			}

			/* Gather the whole packet. We'll have no holes or a partial packet */
			{
				int size=lacing_vals[ptr]&0xff;
				int bytes=0;

				op.packet_base=body_data;
				op.packet=body_returned;
				op.e_o_s=lacing_vals[ptr]&0x200; /* Last packet of the stream? */
				op.b_o_s=lacing_vals[ptr]&0x100; /* First packet of the stream? */
				bytes+=size;

				while (size==255)
				{
					int val=lacing_vals[++ptr];
					size=val&0xff;

					if((val & 0x200) != 0)
						op.e_o_s = 0x200;

					bytes+=size;
				}

				op.packetno=packetno;
				op.granulepos=granule_vals[ptr];
				op.bytes=bytes;

				body_returned+=bytes;

				lacing_returned=ptr+1;
			}

			packetno++;
			return 1;
		}

		/// <summary>
		/// Adds a complete page to the bitstream.
		/// </summary>
		/// <param name="og">The page to insert into the bitstream.</param>
		/// <returns>
		/// -1 indicates failure. This means that the serial number of the page did not match the serial number of the bitstream, the page version was incorrect, or an internal error occurred.
		/// 0 means that the page was successfully submitted to the bitstream.
		/// </returns>
		public int pagein(Page og)
		{
			byte[] header_base=og.header_base;
			int header=og.header;
			byte[] body_base=og.body_base;
			int body=og.body;
			int bodysize=og.body_len;
			int segptr=0;

			int version=og.version();
			int continued=og.continued();
			int bos=og.bos();
			int eos=og.eos();
			long granulepos=og.granulepos();
			int _serialno=og.serialno();
			int _pageno=og.pageno();
			int segments=header_base[header+26]&0xff;

			// Clean up 'returned data'
			{
				int lr=lacing_returned;
				int br=body_returned;

				// Body data

				if (br != 0)
				{
					body_fill-=br;
					if(body_fill!=0)
					{
						Array.Copy(body_data, br, body_data, 0, body_fill);
					}
					body_returned=0;
				}

				if (lr != 0)
				{
					// Segment table
					if ((lacing_fill-lr)!=0)
					{
						Array.Copy(lacing_vals, lr, lacing_vals, 0, lacing_fill-lr);
						Array.Copy(granule_vals, lr, granule_vals, 0, lacing_fill-lr);
					}
					lacing_fill-=lr;
					lacing_packet-=lr;
					lacing_returned=0;
				}
			}

			// Check the serial number
			if (_serialno != serialno) return -1;
			if (version > 0)           return -1;

			lacing_expand(segments+1);

			// Are we in sequence?
			if (_pageno != pageno)
			{
				// Unroll previous partial packet (if any)
				for (int i=lacing_packet;i<lacing_fill;i++)
				{
					body_fill-=lacing_vals[i]&0xff;
				}
				lacing_fill=lacing_packet;

				// Make a note of dropped data in segment table
				if (pageno != -1)
				{
					lacing_vals[lacing_fill++]=0x400;
					lacing_packet++;
				}

				// Are we a 'continued packet' page?  If so, we'll need to skip some segments
				if (continued != 0)
				{
					bos=0;
					for (;segptr < segments; segptr++)
					{
						int val = (header_base[header+27+segptr] & 0xff);
						body += val;
						bodysize -= val;
						if (val < 255)
						{
							segptr++;
							break;
						}
					}
				}
			}

			if (bodysize != 0)
			{
				body_expand(bodysize);
				Array.Copy(body_base, body, body_data, body_fill, bodysize);
				body_fill+=bodysize;
			}

		{
			int saved = -1;
			while (segptr < segments)
			{
				int val = (header_base[header+27+segptr] & 0xff);
				lacing_vals[lacing_fill] = val;
				granule_vals[lacing_fill] = -1;

				if (bos != 0)
				{
					lacing_vals[lacing_fill] |= 0x100;
					bos = 0;
				}

				if (val < 255)
					saved = lacing_fill;

				lacing_fill++;
				segptr++;

				if (val < 255)
					lacing_packet=lacing_fill;
			}

			/* Set the granulepos on the last pcmval of the last full packet */
			if (saved != -1)
			{
				granule_vals[saved]=granulepos;
			}
		}

			if (eos != 0)
			{
				e_o_s = 1;
				if (lacing_fill > 0)
					lacing_vals[lacing_fill - 1] |= 0x200;
			}

			pageno = _pageno+1;
			return 0;
		}

		/// <summary>
		/// Checks for remaining packets inside the stream and forces remaining packets
		/// into a page, regardless of the size of the page.
		/// 
		/// This should only be used when you want to flush an undersized page from the
		/// middle of the stream. Otherwise, pageout or pageout_fill should always be used.
		/// </summary>
		/// <remarks>
		/// Can also be used to verify that all packets have been flushed.
		/// If the return value is 0, all packets have been placed into a page.
		/// Like pageout, it should generally be called in a loop until available
		/// packet data has been flushes, since even a single packet may span multiple pages. 
		/// </remarks>
		/// <param name="og">The page to flush the remaining data into (if present).</param>
		/// <returns>
		/// 0 means that all packet data has already been flushed into pages, and there are no 
		/// packets to put into the page. 0 is also returned in the case of an StreamState
		/// that has been cleared explicitly or implicitly due to an internal error.
		/// 
		/// Nonzero means that remaining packets have successfully been flushed into the page.
		/// </returns>
		public int flush(Page og)
		{
			int i;
			int vals=0;
			int maxvals=(lacing_fill>255?255:lacing_fill);
			int bytes=0;
			int acc=0;
			long granule_pos=granule_vals[0];

			if (maxvals == 0)
				return 0;

			/* Construct a page */
			/* Decide how many segments to include */

			/* If this is the initial header case, the first page must only include the initial header packet */
			if (b_o_s == 0)
			{  /* 'initial header page' case */
				granule_pos=0;
				for (vals=0;vals<maxvals;vals++)
				{
					if ((lacing_vals[vals]&0x0ff)<255)
					{
						vals++;
						break;
					}
				}
			}
			else
			{
				for (vals=0;vals<maxvals;vals++)
				{
					if (acc > 4096)
						break;

					acc+=(lacing_vals[vals]&0x0ff);
					granule_pos=granule_vals[vals];
				}
			}

			/* Construct the header in temp storage */

			byte[] oggs_byt = Encoding.UTF8.GetBytes("OggS");
			Array.Copy(oggs_byt, 0, header, 0, oggs_byt.Length);

			/* Stream structure version */
			header[4]=0x00;

			/* Continued packet flag? */
			header[5]=0x00;
			if ((lacing_vals[0]&0x100) == 0)
				header[5] |= 0x01;

			/* First page flag? */
			if (b_o_s == 0)
				header[5] |= 0x02;

			/* Last page flag? */
			if (e_o_s!=0 && lacing_fill==vals)
				header[5] |= 0x04;
			b_o_s=1;

			/* 64 bits of PCM position */
			for (i=6;i<14;i++)
			{
				header[i]=(byte)granule_pos;
				granule_pos>>=8;
			}

			/* 32 bits of stream serial number */
			{
				int _serialno=serialno;
				for (i=14;i<18;i++)
				{
					header[i]=(byte)_serialno;
					_serialno>>=8;
				}
			}

			// 32 bits of page counter (we have both counter and page header because this value can roll over)
			// because someone called stream_reset; this would be a strange thing to do in an encode stream,
			// but it has plausible uses
			if (pageno==-1)
				pageno = 0;
			{
				int _pageno = pageno++;
				for (i=18;i<22;i++)
				{
					header[i]=(byte)_pageno;
					_pageno>>=8;
				}
			}

			/* Zero for computation; filled in later */
			header[22]=0;
			header[23]=0;
			header[24]=0;
			header[25]=0;

			/* Segment table */
			header[26]=(byte)vals;
			for (i=0;i<vals;i++)
			{
				header[i+27]=(byte)lacing_vals[i];
				bytes+=(header[i+27]&0xff);
			}

			/* Set pointers in the ogg_page struct */
			og.header_base=header;
			og.header=0;
			og.header_len=header_fill=vals+27;
			og.body_base=body_data;
			og.body=body_returned;
			og.body_len=bytes;

			/* Advance the lacing data and set the body_returned pointer */
			lacing_fill-=vals;
			Array.Copy(lacing_vals, vals, lacing_vals, 0, lacing_fill*4);
			Array.Copy(granule_vals, vals, granule_vals, 0, lacing_fill*8);
			body_returned+=bytes;

			/* Calculate the checksum */
			og.checksum();

			/* done */
			return 1;
		}

		/// <summary>
		/// Forms packets into pages
		/// </summary>
		/// <remarks>
		/// In a typical encoding situation, this would be called after using packetin()
		/// to submit data packets to the bitstream. Internally, this function assembles the accumulated 
		/// packet bodies into an Ogg page suitable for writing to a stream. The function is typically 
		/// called in a loop until there are no more pages ready for output.
		/// 
		/// This function will only return a page when a "reasonable" amount of packet data is available.
		/// Normally this is appropriate since it limits the overhead of the Ogg page headers in the bitstream,
		/// and so calling pageout() after packetin() should be the common case.
		/// Call flush() if immediate page generation is desired. This may be occasionally necessary,
		/// for example, to limit the temporal latency of a variable bitrate stream.
		/// </remarks>
		/// <param name="og">The page to fill in with data.</param>
		/// <returns>
		/// Zero means that insufficient data has accumulated to fill a page, or an
		/// internal error occurred. In this case og is not modified.
		/// 
		/// Non-zero means that a page has been completed and returned.
		/// </returns>
		public int pageout(Page og)
		{
			if ((e_o_s!=0&&lacing_fill!=0)     ||   // 'were done, now flush' case
				body_fill-body_returned> 4096  ||   // 'page nominal size' case
				lacing_fill>=255               ||   // 'segment table full' case
				(lacing_fill!=0&&b_o_s==0))
			{  /* 'initial header page' case */
				return flush(og);
			}

			return 0;
		}

		/// <summary>
		/// Gets whether or not we have reached the end of the stream or not. 
		/// </summary>
		/// <returns>
		/// 0 if we have not yet reached the end of the stream.
		/// 1 if we are at the end of the stream or an internal error occurred.
		/// </returns>
		public int eof()
		{
			return e_o_s;
		}

		/// <summary>
		/// Resets values in this StreamState back to their defaults.
		/// </summary>
		/// <returns>0 indicates success. Nonzero is returned on internal error.</returns>
		public int reset()
		{
			body_fill=0;
			body_returned=0;

			lacing_fill=0;
			lacing_packet=0;
			lacing_returned=0;

			header_fill=0;

			e_o_s=0;
			b_o_s=0;
			pageno=-1;
			packetno=0;
			granulepos=0;
			return 0;
		}
	}
}
