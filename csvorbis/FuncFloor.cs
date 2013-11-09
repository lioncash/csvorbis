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
	/// Vorbis encodes a spectral 'floor' vector for each PCM channel.
	/// This vector is a low-resolution representation of the audio spectrum
	/// for the given channel in the current frame, generally used akin to a
	/// whitening filter.
	/// 
	/// It is named a 'floor' because the Xiph.Org reference encoder has historically
	/// used it as a unit-baseline for spectral resolution.
	/// 
	/// <remarks>
	/// The values coded/decoded by a floor are both compactly formatted and make use
	/// of entropy coding to save space. For this reason, a floor configuration generally
	/// refers to multiple codebooks in the codebook component list. Entropy coding is thus
	/// provided as an abstraction, and each floor instance may choose from any and all available
	/// codebooks when coding/decoding.
	/// </remarks>
	/// </summary>
	internal abstract class FuncFloor
	{
		public static FuncFloor[] floor_P = {new Floor0(), new Floor1()};

		public abstract void pack(Object i, csBuffer opb);
		public abstract Object unpack(Info vi, csBuffer opb);
		public abstract Object look(DspState vd, InfoMode mi, Object i);
		public abstract void free_info(Object i);
		public abstract void free_look(Object i);
		public abstract void free_state(Object vs);
		public abstract int forward(Block vb, Object i, float[] fin, float[] fout, Object vs);
		public abstract Object inverse1(Block vb, Object i, Object memo);
		public abstract int inverse2(Block vb, Object i, Object memo, float[] fout);
	}
}
