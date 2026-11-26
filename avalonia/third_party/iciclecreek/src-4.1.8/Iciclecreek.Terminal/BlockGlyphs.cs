using System;
using System.Collections.Generic;

namespace Iciclecreek.Terminal;

internal static class BlockGlyphs
{
	internal readonly record struct Shape(byte X0, byte Y0, byte X1, byte Y1, float Alpha = 1f, byte[]? Points = null)
	{
		public bool IsPolygon => Points != null;
	}

	internal const int Units = 24;

	private static readonly Dictionary<int, Shape[]> Shapes = Build();

	private const byte None = 0;

	private const byte Light = 1;

	private const byte Heavy = 2;

	public static bool IsBlock(int codePoint)
	{
		if ((codePoint < 9600 || codePoint > 9631) && (codePoint < 129792 || codePoint > 129931))
		{
			if (codePoint >= 9472 && codePoint <= 9599)
			{
				return Shapes.ContainsKey(codePoint);
			}
			return false;
		}
		return true;
	}

	public static bool TryGet(int codePoint, out Shape[] shapes)
	{
		if (IsBlock(codePoint) && Shapes.TryGetValue(codePoint, out shapes))
		{
			return true;
		}
		shapes = Array.Empty<Shape>();
		return false;
	}

	private static Dictionary<int, Shape[]> Build()
	{
		Dictionary<int, Shape[]> map = new Dictionary<int, Shape[]>();
		map[9600] = new Shape[1]
		{
			new Shape(0, 0, 24, 12)
		};
		for (int i = 1; i <= 7; i++)
		{
			map[9600 + i] = new Shape[1]
			{
				new Shape(0, (byte)(24 - i * 3), 24, 24)
			};
		}
		map[9608] = new Shape[1]
		{
			new Shape(0, 0, 24, 24)
		};
		for (int num = 7; num >= 1; num--)
		{
			map[9609 + (7 - num)] = new Shape[1]
			{
				new Shape(0, 0, (byte)(num * 3), 24)
			};
		}
		map[9616] = new Shape[1]
		{
			new Shape(12, 0, 24, 24)
		};
		map[9617] = new Shape[1]
		{
			new Shape(0, 0, 24, 24, 0.25f)
		};
		map[9618] = new Shape[1]
		{
			new Shape(0, 0, 24, 24, 0.5f)
		};
		map[9619] = new Shape[1]
		{
			new Shape(0, 0, 24, 24, 0.75f)
		};
		map[9620] = new Shape[1]
		{
			new Shape(0, 0, 24, 3)
		};
		map[9621] = new Shape[1]
		{
			new Shape(21, 0, 24, 24)
		};
		int[] array = new int[10] { 4, 8, 1, 13, 9, 7, 11, 2, 6, 14 };
		for (int j = 0; j < array.Length; j++)
		{
			map[9622 + j] = Grid(array[j], 2, 2);
		}
		for (int k = 0; k < 60; k++)
		{
			int num2 = k + 1;
			if (num2 >= 21)
			{
				num2++;
			}
			if (num2 >= 42)
			{
				num2++;
			}
			map[129792 + k] = Grid(num2, 2, 3);
		}
		Wedge(129852, new byte[6] { 0, 16, 0, 24, 12, 24 });
		Wedge(129853, new byte[6] { 0, 16, 0, 24, 24, 24 });
		Wedge(129854, new byte[6] { 0, 8, 0, 24, 12, 24 });
		Wedge(129855, new byte[6] { 0, 8, 0, 24, 24, 24 });
		Wedge(129856, new byte[6] { 0, 0, 0, 24, 12, 24 });
		Wedge(129857, new byte[10] { 12, 0, 24, 0, 24, 24, 0, 24, 0, 8 });
		Wedge(129858, new byte[8] { 24, 0, 24, 24, 0, 24, 0, 8 });
		Wedge(129859, new byte[10] { 12, 0, 24, 0, 24, 24, 0, 24, 0, 16 });
		Wedge(129860, new byte[8] { 24, 0, 24, 24, 0, 24, 0, 16 });
		Wedge(129861, new byte[8] { 12, 0, 24, 0, 24, 24, 0, 24 });
		Wedge(129862, new byte[8] { 0, 16, 24, 8, 24, 24, 0, 24 });
		Wedge(129863, new byte[6] { 12, 24, 24, 16, 24, 24 });
		Wedge(129864, new byte[6] { 0, 24, 24, 16, 24, 24 });
		Wedge(129865, new byte[6] { 12, 24, 24, 8, 24, 24 });
		Wedge(129866, new byte[6] { 0, 24, 24, 8, 24, 24 });
		Wedge(129867, new byte[6] { 12, 24, 24, 0, 24, 24 });
		Wedge(129868, new byte[10] { 0, 0, 12, 0, 24, 8, 24, 24, 0, 24 });
		Wedge(129869, new byte[8] { 0, 0, 24, 8, 24, 24, 0, 24 });
		Wedge(129870, new byte[10] { 0, 0, 12, 0, 24, 16, 24, 24, 0, 24 });
		Wedge(129871, new byte[8] { 0, 0, 24, 16, 24, 24, 0, 24 });
		Wedge(129872, new byte[8] { 0, 0, 12, 0, 24, 24, 0, 24 });
		Wedge(129873, new byte[8] { 0, 8, 24, 16, 24, 24, 0, 24 });
		Wedge(129874, new byte[10] { 0, 0, 24, 0, 24, 24, 12, 24, 0, 16 });
		Wedge(129875, new byte[8] { 0, 0, 24, 0, 24, 24, 0, 16 });
		Wedge(129876, new byte[10] { 0, 0, 24, 0, 24, 24, 12, 24, 0, 8 });
		Wedge(129877, new byte[8] { 0, 0, 24, 0, 24, 24, 0, 8 });
		Wedge(129878, new byte[8] { 0, 0, 24, 0, 24, 24, 12, 24 });
		Wedge(129879, new byte[6] { 0, 0, 12, 0, 0, 8 });
		Wedge(129880, new byte[6] { 0, 0, 24, 0, 0, 8 });
		Wedge(129881, new byte[6] { 0, 0, 12, 0, 0, 16 });
		Wedge(129882, new byte[6] { 0, 0, 24, 0, 0, 16 });
		Wedge(129883, new byte[6] { 0, 0, 12, 0, 0, 24 });
		Wedge(129884, new byte[8] { 0, 0, 24, 0, 24, 8, 0, 16 });
		Wedge(129885, new byte[10] { 0, 0, 24, 0, 24, 16, 12, 24, 0, 24 });
		Wedge(129886, new byte[8] { 0, 0, 24, 0, 24, 16, 0, 24 });
		Wedge(129887, new byte[10] { 0, 0, 24, 0, 24, 8, 12, 24, 0, 24 });
		Wedge(129888, new byte[8] { 0, 0, 24, 0, 24, 8, 0, 24 });
		Wedge(129889, new byte[8] { 0, 0, 24, 0, 12, 24, 0, 24 });
		Wedge(129890, new byte[6] { 12, 0, 24, 0, 24, 8 });
		Wedge(129891, new byte[6] { 0, 0, 24, 0, 24, 8 });
		Wedge(129892, new byte[6] { 12, 0, 24, 0, 24, 16 });
		Wedge(129893, new byte[6] { 0, 0, 24, 0, 24, 16 });
		Wedge(129894, new byte[6] { 12, 0, 24, 0, 24, 24 });
		Wedge(129895, new byte[8] { 0, 0, 24, 0, 24, 16, 0, 8 });
		byte[] p = new byte[6] { 0, 0, 24, 0, 12, 12 };
		byte[] p2 = new byte[6] { 24, 0, 24, 24, 12, 12 };
		byte[] p3 = new byte[6] { 0, 24, 24, 24, 12, 12 };
		byte[] p4 = new byte[6] { 0, 0, 0, 24, 12, 12 };
		map[129896] = new Shape[3]
		{
			Tri(p),
			Tri(p2),
			Tri(p3)
		};
		map[129897] = new Shape[3]
		{
			Tri(p4),
			Tri(p3),
			Tri(p2)
		};
		map[129898] = new Shape[3]
		{
			Tri(p),
			Tri(p4),
			Tri(p3)
		};
		map[129899] = new Shape[3]
		{
			Tri(p4),
			Tri(p),
			Tri(p2)
		};
		map[129900] = new Shape[1] { Tri(p4) };
		map[129901] = new Shape[1] { Tri(p) };
		map[129902] = new Shape[1] { Tri(p2) };
		map[129903] = new Shape[1] { Tri(p3) };
		for (int l = 1; l <= 6; l++)
		{
			map[129904 + l - 1] = new Shape[1] { Columns(l, l + 1) };
			map[129910 + l - 1] = new Shape[1] { Rows(l, l + 1) };
		}
		map[129916] = new Shape[2]
		{
			Columns(0, 1),
			Rows(7, 8)
		};
		map[129917] = new Shape[2]
		{
			Columns(0, 1),
			Rows(0, 1)
		};
		map[129918] = new Shape[2]
		{
			Columns(7, 8),
			Rows(0, 1)
		};
		map[129919] = new Shape[2]
		{
			Columns(7, 8),
			Rows(7, 8)
		};
		map[129920] = new Shape[2]
		{
			Rows(0, 1),
			Rows(7, 8)
		};
		map[129921] = new Shape[4]
		{
			Rows(0, 1),
			Rows(2, 3),
			Rows(4, 5),
			Rows(7, 8)
		};
		int[] array2 = new int[5] { 2, 3, 5, 6, 7 };
		for (int m = 0; m < array2.Length; m++)
		{
			map[129922 + m] = new Shape[1] { Rows(0, array2[m]) };
		}
		for (int n = 0; n < array2.Length; n++)
		{
			map[129927 + n] = new Shape[1] { Columns(8 - array2[n], 8) };
		}
		BoxDrawing(map);
		return map;
		static Shape Columns(int from, int to)
		{
			return new Shape((byte)(from * 3), 0, (byte)(to * 3), 24);
		}
		static Shape Rows(int from, int to)
		{
			return new Shape(0, (byte)(from * 3), 24, (byte)(to * 3));
		}
		static Shape Tri(byte[] points)
		{
			return new Shape(0, 0, 0, 0, 1f, points);
		}
		void Wedge(int codePoint, params byte[] points)
		{
			map[codePoint] = new Shape[1]
			{
				new Shape(0, 0, 0, 0, 1f, points)
			};
		}
	}

	private static void BoxDrawing(Dictionary<int, Shape[]> map)
	{
		(byte, byte, byte, byte)[] array = new (byte, byte, byte, byte)[76]
		{
			(1, 1, 0, 0),
			(2, 2, 0, 0),
			(0, 0, 1, 1),
			(0, 0, 2, 2),
			default,
			default,
			default,
			default,
			default,
			default,
			default,
			default,
			(0, 1, 0, 1),
			(0, 2, 0, 1),
			(0, 1, 0, 2),
			(0, 2, 0, 2),
			(1, 0, 0, 1),
			(2, 0, 0, 1),
			(1, 0, 0, 2),
			(2, 0, 0, 2),
			(0, 1, 1, 0),
			(0, 2, 1, 0),
			(0, 1, 2, 0),
			(0, 2, 2, 0),
			(1, 0, 1, 0),
			(2, 0, 1, 0),
			(1, 0, 2, 0),
			(2, 0, 2, 0),
			(0, 1, 1, 1),
			(0, 2, 1, 1),
			(0, 1, 2, 1),
			(0, 1, 1, 2),
			(0, 1, 2, 2),
			(0, 2, 2, 1),
			(0, 2, 1, 2),
			(0, 2, 2, 2),
			(1, 0, 1, 1),
			(2, 0, 1, 1),
			(1, 0, 2, 1),
			(1, 0, 1, 2),
			(1, 0, 2, 2),
			(2, 0, 2, 1),
			(2, 0, 1, 2),
			(2, 0, 2, 2),
			(1, 1, 0, 1),
			(2, 1, 0, 1),
			(1, 2, 0, 1),
			(2, 2, 0, 1),
			(1, 1, 0, 2),
			(2, 1, 0, 2),
			(1, 2, 0, 2),
			(2, 2, 0, 2),
			(1, 1, 1, 0),
			(2, 1, 1, 0),
			(1, 2, 1, 0),
			(2, 2, 1, 0),
			(1, 1, 2, 0),
			(2, 1, 2, 0),
			(1, 2, 2, 0),
			(2, 2, 2, 0),
			(1, 1, 1, 1),
			(2, 1, 1, 1),
			(1, 2, 1, 1),
			(2, 2, 1, 1),
			(1, 1, 2, 1),
			(1, 1, 1, 2),
			(1, 1, 2, 2),
			(2, 1, 2, 1),
			(1, 2, 2, 1),
			(2, 1, 1, 2),
			(1, 2, 1, 2),
			(2, 2, 2, 1),
			(2, 2, 1, 2),
			(2, 1, 2, 2),
			(1, 2, 2, 2),
			(2, 2, 2, 2)
		};
		for (int i = 0; i < array.Length; i++)
		{
			var (b, b2, b3, b4) = array[i];
			if (b != 0 || b2 != 0 || b3 != 0 || b4 != 0)
			{
				map[9472 + i] = Arms(b, b2, b3, b4);
			}
		}
		map[9476] = Dashes(horizontal: true, 1, 3);
		map[9477] = Dashes(horizontal: true, 2, 3);
		map[9478] = Dashes(horizontal: false, 1, 3);
		map[9479] = Dashes(horizontal: false, 2, 3);
		map[9480] = Dashes(horizontal: true, 1, 4);
		map[9481] = Dashes(horizontal: true, 2, 4);
		map[9482] = Dashes(horizontal: false, 1, 4);
		map[9483] = Dashes(horizontal: false, 2, 4);
		map[9548] = Dashes(horizontal: true, 1, 2);
		map[9549] = Dashes(horizontal: true, 2, 2);
		map[9550] = Dashes(horizontal: false, 1, 2);
		map[9551] = Dashes(horizontal: false, 2, 2);
		map[9552] = new Shape[2]
		{
			H(8, 10, 0, 24),
			H(14, 16, 0, 24)
		};
		map[9553] = new Shape[2]
		{
			V(8, 10, 0, 24),
			V(14, 16, 0, 24)
		};
		map[9554] = new Shape[3]
		{
			V(11, 13, 8, 24),
			H(8, 10, 11, 24),
			H(14, 16, 11, 24)
		};
		map[9555] = new Shape[3]
		{
			V(8, 10, 11, 24),
			V(14, 16, 11, 24),
			H(11, 13, 8, 24)
		};
		map[9556] = new Shape[4]
		{
			V(8, 10, 8, 24),
			V(14, 16, 14, 24),
			H(8, 10, 8, 24),
			H(14, 16, 14, 24)
		};
		map[9557] = new Shape[3]
		{
			V(11, 13, 8, 24),
			H(8, 10, 0, 13),
			H(14, 16, 0, 13)
		};
		map[9558] = new Shape[3]
		{
			V(8, 10, 11, 24),
			V(14, 16, 11, 24),
			H(11, 13, 0, 16)
		};
		map[9559] = new Shape[4]
		{
			V(14, 16, 8, 24),
			V(8, 10, 14, 24),
			H(8, 10, 0, 16),
			H(14, 16, 0, 10)
		};
		map[9560] = new Shape[3]
		{
			V(11, 13, 0, 16),
			H(8, 10, 11, 24),
			H(14, 16, 11, 24)
		};
		map[9561] = new Shape[3]
		{
			V(8, 10, 0, 13),
			V(14, 16, 0, 13),
			H(11, 13, 8, 24)
		};
		map[9562] = new Shape[4]
		{
			V(8, 10, 0, 16),
			V(14, 16, 0, 10),
			H(14, 16, 8, 24),
			H(8, 10, 14, 24)
		};
		map[9563] = new Shape[3]
		{
			V(11, 13, 0, 16),
			H(8, 10, 0, 13),
			H(14, 16, 0, 13)
		};
		map[9564] = new Shape[3]
		{
			V(8, 10, 0, 13),
			V(14, 16, 0, 13),
			H(11, 13, 0, 16)
		};
		map[9565] = new Shape[4]
		{
			V(14, 16, 0, 16),
			V(8, 10, 0, 10),
			H(14, 16, 0, 16),
			H(8, 10, 0, 10)
		};
		map[9566] = new Shape[3]
		{
			V(11, 13, 0, 24),
			H(8, 10, 11, 24),
			H(14, 16, 11, 24)
		};
		map[9567] = new Shape[3]
		{
			V(8, 10, 0, 24),
			V(14, 16, 0, 24),
			H(11, 13, 14, 24)
		};
		map[9568] = new Shape[5]
		{
			V(8, 10, 0, 24),
			V(14, 16, 0, 10),
			V(14, 16, 14, 24),
			H(8, 10, 14, 24),
			H(14, 16, 14, 24)
		};
		map[9569] = new Shape[3]
		{
			V(11, 13, 0, 24),
			H(8, 10, 0, 13),
			H(14, 16, 0, 13)
		};
		map[9570] = new Shape[3]
		{
			V(8, 10, 0, 24),
			V(14, 16, 0, 24),
			H(11, 13, 0, 10)
		};
		map[9571] = new Shape[5]
		{
			V(14, 16, 0, 24),
			V(8, 10, 0, 10),
			V(8, 10, 14, 24),
			H(8, 10, 0, 10),
			H(14, 16, 0, 10)
		};
		map[9572] = new Shape[3]
		{
			H(8, 10, 0, 24),
			H(14, 16, 0, 24),
			V(11, 13, 14, 24)
		};
		map[9573] = new Shape[3]
		{
			H(11, 13, 0, 24),
			V(8, 10, 11, 24),
			V(14, 16, 11, 24)
		};
		map[9574] = new Shape[5]
		{
			H(8, 10, 0, 24),
			H(14, 16, 0, 10),
			H(14, 16, 14, 24),
			V(8, 10, 14, 24),
			V(14, 16, 14, 24)
		};
		map[9575] = new Shape[3]
		{
			H(8, 10, 0, 24),
			H(14, 16, 0, 24),
			V(11, 13, 0, 10)
		};
		map[9576] = new Shape[3]
		{
			H(11, 13, 0, 24),
			V(8, 10, 0, 13),
			V(14, 16, 0, 13)
		};
		map[9577] = new Shape[5]
		{
			H(14, 16, 0, 24),
			H(8, 10, 0, 10),
			H(8, 10, 14, 24),
			V(8, 10, 0, 10),
			V(14, 16, 0, 10)
		};
		map[9578] = new Shape[3]
		{
			V(11, 13, 0, 24),
			H(8, 10, 0, 24),
			H(14, 16, 0, 24)
		};
		map[9579] = new Shape[3]
		{
			H(11, 13, 0, 24),
			V(8, 10, 0, 24),
			V(14, 16, 0, 24)
		};
		map[9580] = new Shape[8]
		{
			V(8, 10, 0, 10),
			V(8, 10, 14, 24),
			V(14, 16, 0, 10),
			V(14, 16, 14, 24),
			H(8, 10, 0, 10),
			H(8, 10, 14, 24),
			H(14, 16, 0, 10),
			H(14, 16, 14, 24)
		};
		map[9588] = new Shape[1] { H(11, 13, 0, 12) };
		map[9589] = new Shape[1] { V(11, 13, 0, 12) };
		map[9590] = new Shape[1] { H(11, 13, 12, 24) };
		map[9591] = new Shape[1] { V(11, 13, 12, 24) };
		map[9592] = new Shape[1] { H(9, 15, 0, 12) };
		map[9593] = new Shape[1] { V(9, 15, 0, 12) };
		map[9594] = new Shape[1] { H(9, 15, 12, 24) };
		map[9595] = new Shape[1] { V(9, 15, 12, 24) };
		map[9596] = new Shape[2]
		{
			H(11, 13, 0, 12),
			H(9, 15, 12, 24)
		};
		map[9597] = new Shape[2]
		{
			V(11, 13, 0, 12),
			V(9, 15, 12, 24)
		};
		map[9598] = new Shape[2]
		{
			H(9, 15, 0, 12),
			H(11, 13, 12, 24)
		};
		map[9599] = new Shape[2]
		{
			V(9, 15, 0, 12),
			V(11, 13, 12, 24)
		};
		static Shape[] Arms(byte left, byte right, byte up, byte down)
		{
			List<Shape> list = new List<Shape>(4);
			if (left != 0)
			{
				var (y, num) = Stroke(left);
				list.Add(H(y, num, 0, num));
			}
			if (right != 0)
			{
				var (num2, y2) = Stroke(right);
				list.Add(H(num2, y2, num2, 24));
			}
			if (up != 0)
			{
				var (x, num3) = Stroke(up);
				list.Add(V(x, num3, 0, num3));
			}
			if (down != 0)
			{
				var (num4, x2) = Stroke(down);
				list.Add(V(num4, x2, num4, 24));
			}
			return list.ToArray();
		}
		static Shape[] Dashes(bool horizontal, byte weight, int count)
		{
			int num;
			int num2;
			if (weight != 2)
			{
				num = 11;
				num2 = 13;
			}
			else
			{
				num = 9;
				num2 = 15;
			}
			int num3 = 24 / count;
			int num4 = ((count == 2) ? 4 : 2);
			Shape[] array2 = new Shape[count];
			for (int j = 0; j < count; j++)
			{
				int num5 = j * num3 + ((j != 0) ? (num4 / 2) : 0);
				int num6 = (j + 1) * num3 - ((j != count - 1) ? (num4 - num4 / 2) : 0);
				array2[j] = (horizontal ? H(num, num2, num5, num6) : V(num, num2, num5, num6));
			}
			return array2;
		}
		static Shape H(int y0, int y1, int x0, int x1)
		{
			return new Shape((byte)x0, (byte)y0, (byte)x1, (byte)y1);
		}
		static (int Near, int Far) Stroke(byte weight)
		{
			if (weight != 2)
			{
				return (Near: 11, Far: 13);
			}
			return (Near: 9, Far: 15);
		}
		static Shape V(int x0, int x1, int y0, int y1)
		{
			return new Shape((byte)x0, (byte)y0, (byte)x1, (byte)y1);
		}
	}

	private static Shape[] Grid(int bits, int columns, int rows)
	{
		List<Shape> list = new List<Shape>();
		int num = 24 / columns;
		int num2 = 24 / rows;
		for (int i = 0; i < rows; i++)
		{
			for (int j = 0; j < columns; j++)
			{
				if ((bits & (1 << i * columns + j)) != 0)
				{
					list.Add(new Shape((byte)(j * num), (byte)(i * num2), (byte)((j + 1) * num), (byte)((i + 1) * num2)));
				}
			}
		}
		return list.ToArray();
	}
}
