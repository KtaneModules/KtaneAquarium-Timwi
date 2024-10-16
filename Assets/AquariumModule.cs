using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Aquarium;
using UnityEngine;

using Rnd = UnityEngine.Random;

#pragma warning disable IDE0051 // Remove unused private members

/// <summary>
/// On the Subject of Aquarium
/// Created by JakkOfKlubs & Timwi
/// </summary>
public class AquariumModule : MonoBehaviour
{
    public KMBombInfo Bomb;
    public KMBombModule Module;
    public KMAudio Audio;
    public Mesh Highlight;

    public KMSelectable[] squaresSelectables;
    public MeshRenderer[] squareFronts;
    public GameObject[] borders;
    public Material[] squareMats;
    public TextMesh[] colTexts;
    public TextMesh[] rowTexts;
    public KMSelectable ResetButton;

    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private bool _moduleSolved = false;

    const int _w = 6;
    const int _h = 6;

    // 0 = unfilled; 1 = water; 2 = air
    private int[] _squareData = new int[_w * _h];
    private bool[] _solution;

    void Start()
    {
        _moduleId = _moduleIdCounter++;

        for (int i = 0; i < squaresSelectables.Length; i++)
            squaresSelectables[i].OnInteract = PressSquareHandler(i);
        UpdateVisuals();

        ResetButton.OnInteract = ResetButtonPressed;

        GenerateAquarium();
    }

    private void UpdateVisuals()
    {
        for (int square = 0; square < squaresSelectables.Length; square++)
            squareFronts[square].sharedMaterial = squareMats[_squareData[square]];
    }

    private bool ResetButtonPressed()
    {
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, ResetButton.transform);
        ResetButton.AddInteractionPunch();
        if (!_moduleSolved)
        {
            _squareData = new int[_w * _h];
            UpdateVisuals();
        }
        return false;
    }

    private KMSelectable.OnInteractHandler PressSquareHandler(int square)
    {
        return delegate
        {
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, squaresSelectables[square].transform);
            squaresSelectables[square].AddInteractionPunch(.25f);

            if (_moduleSolved)
                return false;

            _squareData[square] = (_squareData[square] + 1) % 3;
            UpdateVisuals();

            // Check if solved
            for (var i = 0; i < _w * _h; i++)
                if (_squareData[i] != (_solution[i] ? 1 : 2))
                    return false;

            Debug.LogFormat("[Aquarium #{0}] Module solved.", _moduleId);
            _moduleSolved = true;
            Module.HandlePass();

            return false;
        };
    }

    private static IEnumerable<int> Orthogonal(int cell, int gridWidth = _w, int gridHeight = _h)
    {
        var x = cell % gridWidth;
        var y = cell / gridHeight;
        for (var xx = x - 1; xx <= x + 1; xx++)
            if (xx >= 0 && xx < gridWidth)
                for (var yy = y - 1; yy <= y + 1; yy++)
                    if (yy >= 0 && yy < gridHeight && (xx == x || yy == y) && (xx != x || yy != y))
                        yield return xx + gridWidth * yy;
    }

    public static bool[] GenerateContiguousRegion(int maxSize, bool[] forbidden)
    {
        var region = new bool[_w * _h];
        var allowed = forbidden.SelectIndexWhere(b => !b).ToArray();
        region[allowed[Rnd.Range(0, allowed.Length)]] = true;
        var curSize = 1;

        while (curSize < maxSize)
        {
            var adjs = new HashSet<int>();
            for (var i = 0; i < _w * _h; i++)
                if (region[i])
                    foreach (var adj in Orthogonal(i))
                        if (!region[adj] && !forbidden[adj])
                            adjs.Add(adj);
            if (adjs.Count == 0)
                return region;
            var adjsArr = adjs.ToArray();
            region[adjsArr[Rnd.Range(0, adjsArr.Length)]] = true;
            curSize++;
        }
        return region;
    }

    struct RegionInfo
    {
        public bool[] Region;
        public int[] Cells;
        public int TopRow { get; private set; }
        public int BottomRow { get; private set; }
        public int NumRows { get; private set; }

        public RegionInfo(bool[] region)
        {
            Region = region;
            Cells = region.SelectIndexWhere(b => b).OrderBy(c => c).ToArray();
            TopRow = Cells[0] / _w;
            BottomRow = Cells.Last() / _w;
            NumRows = BottomRow - TopRow + 1;
        }

        public RegionInfo(int[] cells) : this(Enumerable.Range(0, _w * _h).Select(i => cells.Contains(i)).ToArray())
        {
        }
    }

    private static bool[] MakeSolutionBoard(List<RegionInfo> regions, int[] solutionLevels)
    {
        var solutionBoard = new bool[_w * _h];
        for (var regIx = 0; regIx < regions.Count; regIx++)
            foreach (var cell in regions[regIx].Cells)
                if (cell / _w > regions[regIx].BottomRow - solutionLevels[regIx])
                    solutionBoard[cell] = true;
        return solutionBoard;
    }

    public void GenerateAquarium()
    {
        tryAgain:

        // Generate regions
        var regions = new List<RegionInfo>();
        var covered = new bool[_w * _h];
        var numCovered = 0;
        while (numCovered < _w * _h)
        {
            var newRegion = GenerateContiguousRegion(Rnd.Range(0, 12), covered);
            regions.Add(new RegionInfo(newRegion));
            for (var i = 0; i < _w * _h; i++)
                if (newRegion[i])
                {
                    covered[i] = true;
                    numCovered++;
                }
        }

        // Decide on a random solution
        var solutionLevels = regions.Select(reg => Rnd.Range(0, reg.BottomRow - reg.TopRow + 2)).ToArray();
        _solution = MakeSolutionBoard(regions, solutionLevels);

        // Find the horizontal and vertical clues that aren’t 0 or 6
        var horizClues = Enumerable.Range(0, _h).Select(row => Enumerable.Range(0, _w).Count(col => _solution[col + _w * row])).Select(cl => cl == 0 || cl == _w ? (int?) null : cl).ToArray();
        var vertClues = Enumerable.Range(0, _w).Select(col => Enumerable.Range(0, _h).Count(row => _solution[col + _w * row])).Select(cl => cl == 0 || cl == _h ? (int?) null : cl).ToArray();

        // Ensure that this puzzle isn’t ambiguous with all those clues present
        if (solveRecurse(regions, new int?[regions.Count], horizClues, vertClues).Skip(1).Any())
            goto tryAgain;

        // Determine a minimal set of clues required for the puzzle to be unique
        var allClues = horizClues.Select((clue, index) => clue == null ? null : new { Clue = clue.Value, IsRow = true, Index = index })
            .Concat(vertClues.Select((clue, index) => clue == null ? null : new { Clue = clue.Value, IsRow = false, Index = index }))
            .Where(cl => cl != null)
            .ToArray()
            .Shuffle();
        var requiredClues = Ut.ReduceRequiredSet(Enumerable.Range(0, allClues.Length).ToArray(), test: state =>
        {
            var horizCl = new int?[_h];
            var vertCl = new int?[_w];
            foreach (var clueIx in state.SetToTest)
                (allClues[clueIx].IsRow ? horizCl : vertCl)[allClues[clueIx].Index] = allClues[clueIx].Clue;
            return !solveRecurse(regions, new int?[regions.Count], horizCl, vertCl).Skip(1).Any();
        })
            .Select(cIx => allClues[cIx]).ToArray();

        // Puzzle generated! Set the text objects to display the numbers
        for (var col = 0; col < _w; col++)
        {
            var clue = requiredClues.FirstOrDefault(cl => !cl.IsRow && cl.Index == col);
            colTexts[col].text = clue == null ? "" : clue.Clue.ToString();
        }
        for (var row = 0; row < _h; row++)
        {
            var clue = requiredClues.FirstOrDefault(cl => cl.IsRow && cl.Index == row);
            rowTexts[row].text = clue == null ? "" : clue.Clue.ToString();
        }

        // Set the borders to show the regions
        for (var cell = 0; cell < (_w - 1) * _h; cell++)
        {
            var leftCell = cell % (_w - 1) + _w * (cell / (_w - 1));
            var rightCell = cell % (_w - 1) + 1 + _w * (cell / (_w - 1));
            if (regions.IndexOf(reg => reg.Region[leftCell]) == regions.IndexOf(reg => reg.Region[rightCell]))
                borders[cell].SetActive(false);
        }
        for (var cell = 0; cell < _w * (_h - 1); cell++)
        {
            var aboveCell = cell % _w + _w * (cell / _w);
            var belowCell = cell % _w + _w * (cell / _w + 1);
            if (regions.IndexOf(reg => reg.Region[aboveCell]) == regions.IndexOf(reg => reg.Region[belowCell]))
                borders[30 + cell].SetActive(false);
        }

        // Generate SVG for logging
        var horizLines = string.Format("<path class='aquarium-line' d='{0}' stroke-width='.02' />", Enumerable.Range(1, _h - 1).Select(row => string.Format("M0 {0}h{1}", row, _w)).Join(""));
        var vertLines = string.Format("<path class='aquarium-line' d='{0}' stroke-width='.02' />", Enumerable.Range(1, _h - 1).Select(col => string.Format("M{0} 0v{1}", col, _h)).Join(""));
        var outlines = regions.Select(rg => string.Format("<path class='aquarium-line' d='{0}' stroke-width='.07' />", GenerateSvgPath(rg.Cells, _w, _h, 0, 0))).Join("");
        var cluesSvg = requiredClues.Select(cl => string.Format("<text class='aquarium-number' x='{0}' y='{1}' text-anchor='{2}'>{3}</text>",
            cl.IsRow ? _w + .2 : cl.Index + .5, cl.IsRow ? cl.Index + .7 : -.2, cl.IsRow ? "start" : "middle", cl.Clue)).Join("");
        var blueSquares = Enumerable.Range(0, _w * _h).Select(cell => _solution[cell] ? string.Format("<rect class='aquarium-water' x='{0}' y='{1}' width='1' height='1' />", cell % _w, cell / _w) : "").Join("");
        var svg = string.Format("<svg viewBox='-.1 -1.1 {0} {1}' xmlns='http://www.w3.org/2000/svg' fill='none' font-size='1'>{2}{3}{4}{5}{6}</svg>", _w + 1.2, _h + 1.2, blueSquares, horizLines, vertLines, outlines, cluesSvg);
        Debug.LogFormat("[Aquarium #{0}]=svg[Solution:]{1}", _moduleId, svg);
    }

    private static IEnumerable<int[]> solveRecurse(List<RegionInfo> regions, int?[] sofar, int?[] horizClues, int?[] vertClues)
    {
        var bestRegIx = -1;
        var smallestSize = _h + 1;
        for (var r = 0; r < sofar.Length; r++)
        {
            if (sofar[r] != null)
                continue;
            if (regions[r].NumRows < smallestSize)
            {
                smallestSize = regions[r].NumRows;
                bestRegIx = r;
                if (smallestSize == 1)
                    goto shortcut;
            }
        }
        if (bestRegIx == -1)
        {
            yield return sofar.Select(i => i.Value).ToArray();
            yield break;
        }

        shortcut:
        for (var level = 0; level <= smallestSize; level++)
        {
            sofar[bestRegIx] = level;
            var horizMin = new int[_h];
            var horizMax = new int[_h];
            var vertMin = new int[_w];
            var vertMax = new int[_w];
            for (var regIx = 0; regIx < regions.Count; regIx++)
            {
                foreach (var cell in regions[regIx].Cells)
                {
                    if (sofar[regIx] == null)
                    {
                        horizMax[cell / _w]++;
                        vertMax[cell % _w]++;
                    }
                    else if (cell / _w > regions[regIx].BottomRow - sofar[regIx].Value)
                    {
                        horizMin[cell / _w]++;
                        horizMax[cell / _w]++;
                        vertMin[cell % _w]++;
                        vertMax[cell % _w]++;
                    }
                }
            }
            for (var row = 0; row < _h; row++)
                if (horizClues[row] != null && (horizMin[row] > horizClues[row].Value || horizMax[row] < horizClues[row].Value))
                    goto busted;
            for (var col = 0; col < _h; col++)
                if (vertClues[col] != null && (vertMin[col] > vertClues[col].Value || vertMax[col] < vertClues[col].Value))
                    goto busted;

            foreach (var solution in solveRecurse(regions, sofar, horizClues, vertClues))
                yield return solution;

            busted:;
        }
        sofar[bestRegIx] = null;
    }

    #region Algorithm to generate outlines around cells
    private enum CellDirection { Up, Right, Down, Left }

    struct XY
    {
        public int X;
        public int Y;
        public XY(int x, int y) { X = x; Y = y; }
    }

    public static string GenerateSvgPath(int[] cells, int w, int h, double marginX, double marginY, double? gapX = null, double? gapY = null)
    {
        var outlines = new List<XY[]>();
        var visitedUpArrow = new bool[w][];
        for (var i = 0; i < h; i++)
            visitedUpArrow[i] = new bool[h];

        for (int i = 0; i < w; i++)
            for (int j = 0; j < h; j++)
                // every region must have at least one up arrow (left edge)
                if (!visitedUpArrow[i][j] && get(cells, w, h, i, j) && !get(cells, w, h, i - 1, j))
                    outlines.Add(tracePolygon(cells, w, h, i, j, visitedUpArrow));

        var path = new StringBuilder();

        foreach (var outline in outlines)
        {
            path.Append("M");
            var offset = outline.MinIndex(c => c.X + w * c.Y) + outline.Length - 1;
            for (int j = 0; j <= outline.Length; j++)
            {
                if (j == outline.Length && gapX == null && gapY == null)
                {
                    path.Append("z");
                    continue;
                }

                var point1 = outline[(j + offset) % outline.Length];
                var point2 = outline[(j + offset + 1) % outline.Length];
                var point3 = outline[(j + offset + 2) % outline.Length];
                var x = point2.X;
                var y = point2.Y;

                var dir1 = getDir(point1, point2);
                var dir2 = getDir(point2, point3);

                // “Outer” corners
                if (dir1 == CellDirection.Up && dir2 == CellDirection.Right) // top left corner
                    path.Append(string.Format(" {0} {1}", x + marginX + (j == 0 ? gapX ?? 0 : 0), y + marginY + (j == outline.Length ? gapY ?? 0 : 0)));
                else if (dir1 == CellDirection.Right && dir2 == CellDirection.Down)  // top right corner
                    path.Append(string.Format(" {0} {1}", x - marginX, y + marginY));
                else if (dir1 == CellDirection.Down && dir2 == CellDirection.Left) // bottom right corner
                    path.Append(string.Format(" {0} {1}", x - marginX, y - marginY));
                else if (dir1 == CellDirection.Left && dir2 == CellDirection.Up) // bottom left corner
                    path.Append(string.Format(" {0} {1}", x + marginX, y - marginY));

                // “Inner” corners
                else if (dir1 == CellDirection.Left && dir2 == CellDirection.Down) // top left corner
                    path.Append(string.Format(" {0} {1}", x - marginX, y - marginY));
                else if (dir1 == CellDirection.Up && dir2 == CellDirection.Left) // top right corner
                    path.Append(string.Format(" {0} {1}", x + marginX, y - marginY));
                else if (dir1 == CellDirection.Right && dir2 == CellDirection.Up) // bottom right corner
                    path.Append(string.Format(" {0} {1}", x + marginX, y + marginY));
                else if (dir1 == CellDirection.Down && dir2 == CellDirection.Right) // bottom left corner
                    path.Append(string.Format(" {0} {1}", x - marginX, y + marginY));
            }
        }

        return path.ToString();
    }

    static CellDirection getDir(XY from, XY to)
    {
        return from.X == to.X
            ? (from.Y > to.Y ? CellDirection.Up : CellDirection.Down)
            : (from.X > to.X ? CellDirection.Left : CellDirection.Right);
    }

    static bool get(int[] cells, int w, int h, int x, int y) { return x >= 0 && x < w && y >= 0 && y < h && cells.Contains(x + w * y); }

    static XY[] tracePolygon(int[] cells, int w, int h, int i, int j, bool[][] visitedUpArrow)
    {
        var result = new List<XY>();
        var dir = CellDirection.Up;

        while (true)
        {
            // In each iteration of this loop, we move from the current edge to the next one.
            // We have to prioritise right-turns so that the diagonal-adjacent case is handled correctly.
            // Every time we take a 90° turn, we add the corner coordinate to the result list.
            // When we get back to the original edge, the polygon is complete.
            switch (dir)
            {
                case CellDirection.Up:
                    // If we’re back at the beginning, we’re done with this polygon
                    if (visitedUpArrow[i][j])
                        return result.ToArray();

                    visitedUpArrow[i][j] = true;

                    if (!get(cells, w, h, i, j - 1))
                    {
                        result.Add(new XY(i, j));
                        dir = CellDirection.Right;
                    }
                    else if (get(cells, w, h, i - 1, j - 1))
                    {
                        result.Add(new XY(i, j));
                        dir = CellDirection.Left;
                        i--;
                    }
                    else
                        j--;
                    break;

                case CellDirection.Down:
                    j++;
                    if (!get(cells, w, h, i - 1, j))
                    {
                        result.Add(new XY(i, j));
                        dir = CellDirection.Left;
                        i--;
                    }
                    else if (get(cells, w, h, i, j))
                    {
                        result.Add(new XY(i, j));
                        dir = CellDirection.Right;
                    }
                    break;

                case CellDirection.Left:
                    if (!get(cells, w, h, i - 1, j - 1))
                    {
                        result.Add(new XY(i, j));
                        dir = CellDirection.Up;
                        j--;
                    }
                    else if (get(cells, w, h, i - 1, j))
                    {
                        result.Add(new XY(i, j));
                        dir = CellDirection.Down;
                    }
                    else
                        i--;
                    break;

                case CellDirection.Right:
                    i++;
                    if (!get(cells, w, h, i, j))
                    {
                        result.Add(new XY(i, j));
                        dir = CellDirection.Down;
                    }
                    else if (get(cells, w, h, i, j - 1))
                    {
                        result.Add(new XY(i, j));
                        dir = CellDirection.Up;
                        j--;
                    }
                    break;
            }
        }
    }
    #endregion

#pragma warning disable 414
    private readonly string TwitchHelpMessage = @"!{0} water A1 A2 A3 [set cells to water] | !{0} air B3 B4 C4 [set cells to air] | !{0} reset D1 D2 [set cells to unfilled] | !{0} rest air/water [set unfilled cells to air or water] | !{0} solve ##..###.....# [solve whole puzzle; # = water, . = air]| !{0} reset [reset the whole puzzle]";
#pragma warning restore 414

    KMSelectable[] ProcessTwitchCommand(string command)
    {
        Match m;
        if ((m = Regex.Match(command, @"^\s*(?:(?<water>water|w)|(?<air>air|a)|reset|r)\s+([a-f][1-6]\s*)+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Success)
        {
            var coords = m.Groups[1].Captures.Cast<Capture>().Select(capture => coord(capture.Value.Trim())).ToArray();
            if (coords.Any(c => c == null))
                return null;
            var desiredValue = m.Groups["water"].Success ? 1 : m.Groups["air"].Success ? 2 : 0;
            return coords.SelectMany(c => Enumerable.Repeat(squaresSelectables[c.Value], (3 + desiredValue - _squareData[c.Value]) % 3)).ToArray();
        }

        if (Regex.IsMatch(command, @"^\s*(reset|r)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return new[] { ResetButton };

        if ((m = Regex.Match(command, @"^\s*(?:rest|r)\s+(?:air|(?<w>water))\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Success)
        {
            var presses = _squareData.SelectIndexWhere(c => c == 0).Select(ix => squaresSelectables[ix]);
            return m.Groups["w"].Success ? presses.ToArray() : presses.Concat(presses).ToArray();
        }

        if ((m = Regex.Match(command, @"^\s*(?:solve|s)\s+([\.#]{36})\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Success)
        {
            var btns = new List<KMSelectable>();
            for (var i = 0; i < 36; i++)
                btns.AddRange(Enumerable.Repeat(squaresSelectables[i], (3 + (m.Groups[1].Value[i] == '#' ? 1 : 2) - _squareData[i]) % 3));
            return btns.ToArray();
        }

        return null;
    }

    private int? coord(string unparsed)
    {
        if (unparsed.Length == 2 && "ABCDEFabcdef".Contains(unparsed[0]) && "123456".Contains(unparsed[1]))
            return "ABCDEFabcdef".IndexOf(unparsed[0]) % 6 + 6 * "123456".IndexOf(unparsed[1]);
        return null;
    }

    IEnumerator TwitchHandleForcedSolve()
    {
        if (_moduleSolved)
            yield break;

        for (var cell = 0; cell < 6 * 6; cell++)
        {
            while (_squareData[cell] != (_solution[cell] ? 1 : 2))
            {
                squaresSelectables[cell].OnInteract();
                yield return new WaitForSeconds(.1f);
            }
        }

        while (!_moduleSolved)
            yield return true;
    }
}
