using System;
using System.Collections.Generic;

class Program
{
    static void Print(char[,] a)
    {
        int w = a.GetLength(0), h = a.GetLength(1);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++) Console.Write(a[x,y]);
            Console.WriteLine();
        }
    }

    static char[,] Rotate90CW(char[,] old, HashSet<(int,int)> selection)
    {
        int oldW = old.GetLength(0), oldH = old.GetLength(1);
        int newW = oldH, newH = oldW;
        var n = new char[newW, newH];
        for (int y = 0; y < newH; y++) for (int x = 0; x < newW; x++) n[x,y] = '.';
        for (int sy = 0; sy < oldH; sy++) for (int sx = 0; sx < oldW; sx++)
        {
            int idxX = sx, idxY = sy;
            bool sel = selection == null || selection.Contains((sx,sy));
            if (!sel) continue;
            int dx = (oldH - 1) - sy; int dy = sx; // mapping used in MainWindow
            if (dx>=0 && dx<newW && dy>=0 && dy<newH)
                n[dx,dy] = old[sx,sy];
        }
        return n;
    }

    static char[,] FlipH(char[,] old, HashSet<(int,int)> selection)
    {
        int w = old.GetLength(0), h = old.GetLength(1);
        var n = new char[w,h]; for (int y=0;y<h;y++) for (int x=0;x<w;x++) n[x,y]='.';
        for (int sy=0; sy<h; sy++) for (int sx=0; sx<w; sx++)
        {
            bool sel = selection == null || selection.Contains((sx,sy));
            if (!sel) continue;
            int dx = (w - 1) - sx; int dy = sy;
            n[dx,dy] = old[sx,sy];
        }
        return n;
    }

    static char[,] FlipV(char[,] old, HashSet<(int,int)> selection)
    {
        int w = old.GetLength(0), h = old.GetLength(1);
        var n = new char[w,h]; for (int y=0;y<h;y++) for (int x=0;x<w;x++) n[x,y]='.';
        for (int sy=0; sy<h; sy++) for (int sx=0; sx<w; sx++)
        {
            bool sel = selection == null || selection.Contains((sx,sy));
            if (!sel) continue;
            int dx = sx; int dy = (h - 1) - sy;
            n[dx,dy] = old[sx,sy];
        }
        return n;
    }

    static void Main()
    {
        // User pattern: 5x4
        // Represent 'O' tiles and 'x' empties
        char[,] a = new char[5,4]
        {
            { 'x','x','x','x' },
            { 'x','x','x','x' },
            { 'O','O','O','O' },
            { 'x','x','O','O' },
            { 'x','x','x','x' }
        };
        // Above is not correct layout; construct from user's text explicitly
        var src = new string[] {
            "xxOxx",
            "xxOxx",
            "xOOOx",
            "xOOOx"
        };
        int W = src[0].Length, H = src.Length;
        var grid = new char[W,H];
        for (int y=0;y<H;y++) for (int x=0;x<W;x++) grid[x,y] = src[y][x];

        Console.WriteLine("Original:"); Print(grid);

        // Build selection set: wand selects 'O' cells
        var sel = new HashSet<(int,int)>();
        for (int y=0;y<H;y++) for (int x=0;x<W;x++) if (grid[x,y]=='O') sel.Add((x,y));

        Console.WriteLine("Rotate 90 CW (selected cells only):");
        var r = Rotate90CW(grid, sel);
        Print(r);

        Console.WriteLine("Flip Horizontal (selected cells only):");
        var fh = FlipH(grid, sel);
        Print(fh);

        Console.WriteLine("Flip Vertical (selected cells only):");
        var fv = FlipV(grid, sel);
        Print(fv);
    }
}
