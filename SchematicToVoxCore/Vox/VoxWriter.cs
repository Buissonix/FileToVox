﻿using FileToVox.Schematics;
using FileToVox.Schematics.Tools;
using FileToVox.Utils;
using SchematicToVoxCore.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FileToVox.Vox.Chunks;

namespace FileToVox.Vox
{
	public class VoxWriter : VoxParser
	{
		private int _width;
		private int _length;
		private int _height;
		private int _countSize;
		private int _countRegionNonEmpty;
		private int _totalBlockCount;

		private int _countBlocks;
		private int _childrenChunkSize;
		private Schematic _schematic;
		private Rotation _rotation = Rotation._PZ_PX_P;
		private List<BlockGlobal> _firstBlockInEachRegion;
		private List<Color> _usedColors;
		private List<Color> _palette;
		private uint[,,] _blocks;

		private int CHUNK_SIZE = 125;

		public bool WriteModel(int chunkSize, string absolutePath, List<Color> palette, Schematic schematic)
		{
			CHUNK_SIZE = chunkSize;
			_width = _length = _height = _countSize = _totalBlockCount = _countRegionNonEmpty = 0;
			_schematic = schematic;
			_palette = palette;
			_blocks = _schematic.Blocks.To3DArray(schematic);
			using (var writer = new BinaryWriter(File.Open(absolutePath, FileMode.Create)))
			{
				writer.Write(Encoding.UTF8.GetBytes(HEADER));
				writer.Write(VERSION);
				writer.Write(Encoding.UTF8.GetBytes(MAIN));
				writer.Write(0); //MAIN CHUNK has a size of 0
				writer.Write(CountChildrenSize());
				WriteChunks(writer);
			}
			return true;
		}

		/// <summary>
		/// Count the total bytes of all children chunks
		/// </summary>
		/// <returns></returns>
		private int CountChildrenSize()
		{
			_width = (int)Math.Ceiling(((decimal)_schematic.Width / CHUNK_SIZE)) + 1;
			_length = (int)Math.Ceiling(((decimal)_schematic.Length / CHUNK_SIZE)) + 1;
			_height = (int)Math.Ceiling(((decimal)_schematic.Height / CHUNK_SIZE)) + 1;

			_countSize = _width * _length * _height;
			_firstBlockInEachRegion = GetFirstBlockForEachRegion();
			_countRegionNonEmpty = _firstBlockInEachRegion.Count;
			_totalBlockCount = _schematic.Blocks.Count;

			Console.WriteLine("[INFO] Total blocks: " + _totalBlockCount);

			int chunkSize = 24 * _countRegionNonEmpty; //24 = 12 bytes for header and 12 bytes of content
			int chunkXYZI = (16 * _countRegionNonEmpty) + _totalBlockCount * 4; //16 = 12 bytes for header and 4 for the voxel count + (number of voxels) * 4
			int chunknTRNMain = 40; //40 = 
			int chunknGRP = 24 + _countRegionNonEmpty * 4;
			int chunknTRN = 60 * _countRegionNonEmpty;
			int chunknSHP = 32 * _countRegionNonEmpty;
			int chunkRGBA = 1024 + 12;
			int chunkMATL = 256 * 206;

			for (int i = 0; i < _countRegionNonEmpty; i++)
			{
				string pos = GetWorldPosString(i);
				chunknTRN += Encoding.UTF8.GetByteCount(pos);
				chunknTRN += Encoding.UTF8.GetByteCount(Convert.ToString((byte)_rotation));
			}
			_childrenChunkSize = chunkSize; //SIZE CHUNK
			_childrenChunkSize += chunkXYZI; //XYZI CHUNK
			_childrenChunkSize += chunknTRNMain; //First nTRN CHUNK (constant)
			_childrenChunkSize += chunknGRP; //nGRP CHUNK
			_childrenChunkSize += chunknTRN; //nTRN CHUNK
			_childrenChunkSize += chunknSHP;
			_childrenChunkSize += chunkRGBA;
			_childrenChunkSize += chunkMATL;

			return _childrenChunkSize;
		}

		/// <summary>
		/// Get all blocks in the specified coordinates
		/// </summary>
		/// <param name="min"></param>
		/// <param name="max"></param>
		/// <returns></returns>
		private List<Block> GetBlocksInRegion(Vector3 min, Vector3 max)
		{
			List<Block> list = new List<Block>();

			for (int y = (int)min.Y; y < max.Y; y++)
			{
				for (int z = (int)min.Z; z < max.Z; z++)
				{
					for (int x = (int)min.X; x < max.X; x++)
					{
						if (y < _schematic.Height && x < _schematic.Width && z < _schematic.Length && _blocks[x, y, z] != 0)
						{
							Block block = new Block((ushort)x, (ushort)y, (ushort)z, _blocks[x, y, z]);
							list.Add(block);
						}
					}
				}
			}
			//Parallel.ForEach(_schematic.Blocks, block =>
			//{
			//    if (block.X >= min.X && block.Y >= min.Y && block.Z >= min.Z && block.X < max.X && block.Y < max.Y && block.Z < max.Z)
			//    {
			//        concurrent.Add(block);
			//    }
			//});

			return list;
		}

		private bool HasBlockInRegion(Vector3 min, Vector3 max)
		{
			for (int y = (int)min.Y; y < max.Y; y++)
			{
				for (int z = (int)min.Z; z < max.Z; z++)
				{
					for (int x = (int)min.X; x < max.X; x++)
					{
						if (y < _schematic.Height && x < _schematic.Width && z < _schematic.Length && (_blocks[x, y, z] != 0))
						{
							return true;
						}
					}
				}
			}

			return false;
		}

		/// <summary>
		/// Get world coordinates of the first block in each region
		/// </summary>
		private List<BlockGlobal> GetFirstBlockForEachRegion()
		{
			List<BlockGlobal> list = new List<BlockGlobal>();

			//x = Index % XSIZE;
			//y = (Index / XSIZE) % YSIZE;
			//z = Index / (XSIZE * YSIZE);
			Console.WriteLine("[LOG] Started to compute the first block for each region");
			using (ProgressBar progressBar = new ProgressBar())
			{
				for (int i = 0; i < _countSize; i++)
				{
					int x = i % _width;
					int y = (i / _width) % _height;
					int z = i / (_width * _height);
					if (HasBlockInRegion(new Vector3(x * CHUNK_SIZE, y * CHUNK_SIZE, z * CHUNK_SIZE), new Vector3(x * CHUNK_SIZE + CHUNK_SIZE, y * CHUNK_SIZE + CHUNK_SIZE, z * CHUNK_SIZE + CHUNK_SIZE)))
					{
						list.Add(new BlockGlobal(x * CHUNK_SIZE, y * CHUNK_SIZE, z * CHUNK_SIZE));
					}

					progressBar.Report(i / (float)_countSize);
				}
			}

			Console.WriteLine("[LOG] Done.");
			return list;
		}

		/// <summary>
		/// Convert the coordinates of the first block in each region into string
		/// </summary>
		/// <param name="index"></param>
		/// <returns></returns>
		private string GetWorldPosString(int index)
		{
			int worldPosX = _firstBlockInEachRegion[index].X - 938;
			int worldPosZ = _firstBlockInEachRegion[index].Z - 938;
			int worldPosY = _firstBlockInEachRegion[index].Y + CHUNK_SIZE;

			string pos = worldPosZ + " " + worldPosX + " " + worldPosY;
			return pos;
		}

		/// <summary>
		/// Main loop for write all chunks
		/// </summary>
		/// <param name="writer"></param>
		private void WriteChunks(BinaryWriter writer)
		{
			WritePaletteChunk(writer);
			for (int i = 0; i < 256; i++)
			{
				WriteMaterialChunk(writer, i + 1);
			}

			using (ProgressBar progressbar = new ProgressBar())
			{
				Console.WriteLine("[LOG] Started to write chunks ...");
				for (int i = 0; i < _countRegionNonEmpty; i++)
				{
					WriteSizeChunk(writer);
					WriteXyziChunk(writer, i);
					float progress = ((float)i / _countRegionNonEmpty);
					progressbar.Report(progress);
				}
				Console.WriteLine("[LOG] Done.");
			}

			WriteMainTranformNode(writer);
			WriteGroupChunk(writer);
			for (int i = 0; i < _countRegionNonEmpty; i++)
			{
				WriteTransformChunk(writer, i);
				WriteShapeChunk(writer, i);
			}
			Console.WriteLine("[LOG] Check total blocks after conversion: " + _countBlocks);
			if (_totalBlockCount != _countBlocks)
			{
				Console.WriteLine("[ERROR] There is a difference between total blocks before and after conversion.");
			}
		}

		/// <summary>
		/// Write the main trande node chunk
		/// </summary>
		/// <param name="writer"></param>
		private void WriteMainTranformNode(BinaryWriter writer)
		{
			writer.Write(Encoding.UTF8.GetBytes(nTRN));
			writer.Write(28); //Main nTRN has always a 28 bytes size
			writer.Write(0); //Child nTRN chunk size
			writer.Write(0); // ID of nTRN
			writer.Write(0); //ReadDICT size for attributes (none)
			writer.Write(1); //Child ID
			writer.Write(-1); //Reserved ID
			writer.Write(0); //Layer ID
			writer.Write(1); //Read Array Size
			writer.Write(0); //ReadDICT size
		}

		/// <summary>
		/// Write SIZE chunk
		/// </summary>
		/// <param name="writer"></param>
		private void WriteSizeChunk(BinaryWriter writer)
		{
			writer.Write(Encoding.UTF8.GetBytes(SIZE));
			writer.Write(12); //Chunk Size (constant)
			writer.Write(0); //Child Chunk Size (constant)

			writer.Write(CHUNK_SIZE); //Width
			writer.Write(CHUNK_SIZE); //Height
			writer.Write(CHUNK_SIZE); //Depth
		}

		/// <summary>
		/// Write XYZI chunk
		/// </summary>
		/// <param name="writer"></param>
		/// <param name="index"></param>
		private void WriteXyziChunk(BinaryWriter writer, int index)
		{
			writer.Write(Encoding.UTF8.GetBytes(XYZI));
			IEnumerable<Block> blocks = null;

			if (_schematic.Blocks.Count > 0)
			{
				BlockGlobal firstBlock = _firstBlockInEachRegion[index];

				//blocks = _schematic.Blocks.Where(block => block.X >= firstBlock.X && block.Y >= firstBlock.Y && block.Z >= firstBlock.Z && block.X < firstBlock.X + CHUNK_SIZE && block.Y < firstBlock.Y + CHUNK_SIZE && block.Z < firstBlock.Z + CHUNK_SIZE);
				blocks = GetBlocksInRegion(new Vector3(firstBlock.X, firstBlock.Y, firstBlock.Z), new Vector3(firstBlock.X + CHUNK_SIZE, firstBlock.Y + CHUNK_SIZE, firstBlock.Z + CHUNK_SIZE));
			}
			writer.Write((blocks.Count() * 4) + 4); //XYZI chunk size
			writer.Write(0); //Child chunk size (constant)
			writer.Write(blocks.Count()); //Blocks count
			_countBlocks += blocks.Count();

			foreach (Block block in blocks)
			{
				writer.Write((byte)(block.X % CHUNK_SIZE));
				writer.Write((byte)(block.Y % CHUNK_SIZE));
				writer.Write((byte)(block.Z % CHUNK_SIZE));
				if (block.PalettePosition != -1)
				{
					writer.Write((byte)(block.PalettePosition + 1));
				}
				else
				{
					int i = _usedColors.IndexOf(block.Color.UIntToColor()) + 1;
					writer.Write((i != 0) ? (byte)i : (byte)1);
				}

				//_schematic.Blocks.Remove(block);
			}
		}

		/// <summary>
		/// Write nTRN chunk
		/// </summary>
		/// <param name="writer"></param>
		/// <param name="index"></param>
		private void WriteTransformChunk(BinaryWriter writer, int index)
		{
			writer.Write(Encoding.UTF8.GetBytes(nTRN));
			string pos = GetWorldPosString(index);
			writer.Write(48 + Encoding.UTF8.GetByteCount(pos)
							+ Encoding.UTF8.GetByteCount(Convert.ToString((byte)_rotation))); //nTRN chunk size
			writer.Write(0); //nTRN child chunk size
			writer.Write(2 * index + 2); //ID
			writer.Write(0); //ReadDICT size for attributes (none)
			writer.Write(2 * index + 3);//Child ID
			writer.Write(-1); //Reserved ID
			writer.Write(-1); //Layer ID
			writer.Write(1); //Read Array Size
			writer.Write(2); //Read DICT Size (previously 1)

			writer.Write(2); //Read STRING size
			writer.Write(Encoding.UTF8.GetBytes("_r"));
			writer.Write(Encoding.UTF8.GetByteCount(Convert.ToString((byte)_rotation)));
			writer.Write(Encoding.UTF8.GetBytes(Convert.ToString((byte)_rotation)));


			writer.Write(2); //Read STRING Size
			writer.Write(Encoding.UTF8.GetBytes("_t"));
			writer.Write(Encoding.UTF8.GetByteCount(pos));
			writer.Write(Encoding.UTF8.GetBytes(pos));
		}

		/// <summary>
		/// Write nSHP chunk
		/// </summary>
		/// <param name="writer"></param>
		/// <param name="index"></param>
		private void WriteShapeChunk(BinaryWriter writer, int index)
		{
			writer.Write(Encoding.UTF8.GetBytes(nSHP));
			writer.Write(20); //nSHP chunk size
			writer.Write(0); //nSHP child chunk size
			writer.Write(2 * index + 3); //ID
			writer.Write(0);
			writer.Write(1);
			writer.Write(index);
			writer.Write(0);
		}

		/// <summary>
		/// Write nGRP chunk
		/// </summary>
		/// <param name="writer"></param>
		private void WriteGroupChunk(BinaryWriter writer)
		{
			writer.Write(Encoding.UTF8.GetBytes(nGRP));
			writer.Write(16 + (4 * (_countRegionNonEmpty - 1))); //nGRP chunk size
			writer.Write(0); //Child nGRP chunk size
			writer.Write(1); //ID of nGRP
			writer.Write(0); //Read DICT size for attributes (none)
			writer.Write(_countRegionNonEmpty);
			for (int i = 0; i < _countRegionNonEmpty; i++)
			{
				writer.Write((2 * i) + 2); //id for childrens (start at 2, increment by 2)
			}
		}

		/// <summary>
		/// Write RGBA chunk
		/// </summary>
		/// <param name="writer"></param>
		private void WritePaletteChunk(BinaryWriter writer)
		{
			writer.Write(Encoding.UTF8.GetBytes(RGBA));
			writer.Write(1024);
			writer.Write(0);
			_usedColors = new List<Color>(256);
			if (_palette != null)
			{
				_usedColors = _palette;
				foreach (Color color in _usedColors)
				{
					writer.Write(color.R);
					writer.Write(color.G);
					writer.Write(color.B);
					writer.Write(color.A);
				}
			}
			else
			{
				foreach (Block block in _schematic.Blocks)
				{
					Color color = block.Color.UIntToColor();
					if (_usedColors.Count < 256 && !_usedColors.Contains(color))
					{
						_usedColors.Add(color);
						writer.Write(color.R);
						writer.Write(color.G);
						writer.Write(color.B);
						writer.Write(color.A);
					}
				}
			}

			for (int i = (256 - _usedColors.Count); i >= 1; i--)
			{
				writer.Write((byte)0);
				writer.Write((byte)0);
				writer.Write((byte)0);
				writer.Write((byte)0);
			}
		}

		/// <summary>
		/// Write the MATL chunk
		/// </summary>
		/// <param name="writer"></param>
		private int WriteMaterialChunk(BinaryWriter writer, int index)
		{
			int byteWritten = 0;
			writer.Write(Encoding.UTF8.GetBytes(MATL));
			KeyValue[] materialProperties = new KeyValue[12];
			materialProperties[0].Key = "_type";
			materialProperties[0].Value = "_diffuse";

			materialProperties[1].Key = "_weight";
			materialProperties[1].Value = "1";

			materialProperties[2].Key = "_rough";
			materialProperties[2].Value = "0.1";

			materialProperties[3].Key = "_spec";
			materialProperties[3].Value = "0.5";

			materialProperties[4].Key = "_spec_p";
			materialProperties[4].Value = "0.5";

			materialProperties[5].Key = "_ior";
			materialProperties[5].Value = "0.3";

			materialProperties[6].Key = "_att";
			materialProperties[6].Value = "0";

			materialProperties[7].Key = "_g0";
			materialProperties[7].Value = "-0.5";

			materialProperties[8].Key = "_g1";
			materialProperties[8].Value = "0.8";

			materialProperties[9].Key = "_gw";
			materialProperties[9].Value = "0.7";

			materialProperties[10].Key = "_flux";
			materialProperties[10].Value = "0";

			materialProperties[11].Key = "_ldr";
			materialProperties[11].Value = "0";

			writer.Write(GetMaterialPropertiesSize(materialProperties) + 8);
			writer.Write(0); //Child Chunk Size (constant)

			writer.Write(index); //Id
			writer.Write(materialProperties.Length); //ReadDICT size

			byteWritten += Encoding.UTF8.GetByteCount(MATL) + 16;

			foreach (KeyValue keyValue in materialProperties)
			{
				writer.Write(Encoding.UTF8.GetByteCount(keyValue.Key));
				writer.Write(Encoding.UTF8.GetBytes(keyValue.Key));
				writer.Write(Encoding.UTF8.GetByteCount(keyValue.Value));
				writer.Write(Encoding.UTF8.GetBytes(keyValue.Value));

				byteWritten += 8 + Encoding.UTF8.GetByteCount(keyValue.Key) + Encoding.UTF8.GetByteCount(keyValue.Value);
			}

			return byteWritten;
		}

		private int GetMaterialPropertiesSize(KeyValue[] properties)
		{
			return properties.Sum(keyValue => 8 + Encoding.UTF8.GetByteCount(keyValue.Key) + Encoding.UTF8.GetByteCount(keyValue.Value));
		}
	}

	struct BlockGlobal
	{
		public readonly int X;
		public readonly int Y;
		public readonly int Z;

		public BlockGlobal(int x, int y, int z)
		{
			X = x;
			Y = y;
			Z = z;
		}

		public override string ToString()
		{
			return $"{X} {Y} {Z}";
		}
	}
}
