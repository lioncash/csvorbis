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


using System;
using csogg;

namespace csvorbis 
{
	/// <summary>
	/// A mapping contains a channel coupling description and a list of ÅfsubmapsÅf 
	/// that bundle sets of channel vectors together for grouped encoding and decoding. 
	/// These submaps are not references to external components; the submap list is internal
	/// and specific to a mapping.
	/// 
	/// <remarks>
	/// A ÅfsubmapÅf is a configuration/grouping that applies to a subset of floor and residue vectors
	/// within a mapping. The submap functions as a last layer of indirection such that specific special
	/// floor or residue settings can be applied not only to all the vectors in a given mode, but also
	/// specific vectors in a specific mode. Each submap specifies the proper floor and residue instance
	/// number to use for decoding that submapÅfs spectral floor and spectral residue vectors.
	/// </remarks>
	/// 
	/// <example>
	/// Assume a Vorbis stream that contains six channels in the standard 5.1 format.
	/// The sixth channel, as is normal in 5.1, is bass only.
	/// Therefore it would be wasteful to encode a full-spectrum version of it as with the other channels.
	/// The submapping mechanism can be used to apply a full range floor and residue encoding to
	/// channels 0 through 4, and a bass-only representation to the bass channel, thus saving space.
	/// 
	/// In this example, channels 0-4 belong to submap 0 (which indicates use of a full-range floor)
	/// and channel 5 belongs to submap 1, which uses a bass-only representation.
	/// </example>
	/// </summary>
	internal abstract class FuncMapping
	{
		public static FuncMapping[] mapping_P = {new Mapping0()};

		public abstract void pack(Info info, Object imap, csBuffer buffer);
		public abstract Object unpack(Info info, csBuffer buffer);
		public abstract Object look(DspState vd, InfoMode vm, Object m);
		public abstract void free_info(Object imap);
		public abstract void free_look(Object imap);
		public abstract int inverse(Block vd, Object lm);
	}
}