#nullable disable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Drawing.Imaging;


namespace PZ_ChunkWiper
{
    public sealed class MapViewerControl : Control
    {
        
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int TilesPerPixel { get; set; } = 4;

        
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int ChunkTiles { get; set; } = 8;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int CellTiles { get; set; } = 256;

        public int ChunkPx => ChunkTiles / TilesPerPixel; 
        public int CellPx => CellTiles / TilesPerPixel;   

        private float backgroundTilesPerPixel = 1f;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public float BackgroundTilesPerPixel
        {
            get => backgroundTilesPerPixel;
            set
            {
                if (!float.IsFinite(value) || value < .125f || value > 64f) throw new ArgumentOutOfRangeException(nameof(value));
                backgroundTilesPerPixel = value; Invalidate();
            }
        }
        internal RectangleF BackgroundBounds => BackgroundImage == null ? RectangleF.Empty
            : new RectangleF(0, 0, BackgroundImage.Width * BackgroundTilesPerPixel / TilesPerPixel,
                BackgroundImage.Height * BackgroundTilesPerPixel / TilesPerPixel);

        
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public new Image BackgroundImage { get; private set; }
        private Stream backgroundStream;
        private bool animating;
        internal void LoadBackground(string path)
        {
            
            var stream = new MemoryStream(File.ReadAllBytes(path), false);
            Image next;
            try { next = Image.FromStream(stream, false, true); }
            catch { stream.Dispose(); throw; }
            ReleaseBackground(); BackgroundImage = next; backgroundStream = stream;
            animating = ImageAnimator.CanAnimate(next);
            if (animating) ImageAnimator.Animate(next, FrameChanged);
            Invalidate();
        }
        private void FrameChanged(object sender, EventArgs args)
        {
            if (!IsHandleCreated || IsDisposed) return;
            try { BeginInvoke((Action)(() => { if (!IsDisposed) Invalidate(); })); }
            catch (InvalidOperationException) { }
        }
        private void ReleaseBackground()
        {
            if (animating && BackgroundImage != null) ImageAnimator.StopAnimate(BackgroundImage, FrameChanged);
            animating = false; BackgroundImage?.Dispose(); BackgroundImage = null;
            backgroundStream?.Dispose(); backgroundStream = null;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) ReleaseBackground();
            base.Dispose(disposing);
        }

        
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public float Zoom { get; private set; } = 1f;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public PointF Pan { get; private set; } = new PointF(0, 0);

        
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Rectangle? SelectedCellRect { get; set; } 

        
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ShowGridCell { get; set; } = true;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ShowGridChunk { get; set; } = false;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]

        public bool ShowMapChunks { get; set; } = true;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ShowIsoRegionChunks { get; set; } = true;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ShowBlamChunks { get; set; } = true;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]

        public bool ShowChunkDataCells { get; set; } = true;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ShowZPopCells { get; set; } = true;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ShowAPopCells { get; set; } = true;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ShowMetaCells { get; set; } = true;

        
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public HashSet<(int x, int y)> MapChunks { get; } = new HashSet<(int x, int y)>();
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public HashSet<(int x, int y)> IsoRegionChunks { get; } = new HashSet<(int x, int y)>();
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public HashSet<(int x, int y)> BlamChunks { get; } = new HashSet<(int x, int y)>();

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]

        public HashSet<(int cx, int cy)> ChunkDataCells { get; } = new HashSet<(int cx, int cy)>();
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public HashSet<(int cx, int cy)> ZPopCells { get; } = new HashSet<(int cx, int cy)>();
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public HashSet<(int cx, int cy)> APopCells { get; } = new HashSet<(int cx, int cy)>();
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public HashSet<(int cx, int cy)> MetaCells { get; } = new HashSet<(int cx, int cy)>();

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]

        public bool ShowCellCoords { get; set; } = true;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public float CellCoordsMinZoom { get; set; } = 1.2f; 

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]

        public float BackgroundAlpha { get; set; } = 1f; 

        
        private bool _panning;
        private Point _panStartMouse;
        private PointF _panStart;
        private bool _selectingCells;
        private Point _selectionStartCell;

        public event Action<int, int, int, int> CellSelectionChanged;

        static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

        public struct Marker
        {
            public Marker(float px, float py, string label)
            {
                Px = px;
                Py = py;
                Label = label;
            }

            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]

            public float Px { get; }
            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public float Py { get; }
            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public string Label { get; }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]

        public bool ShowVehicles { get; set; } = true;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ShowLocalPlayers { get; set; } = true;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ShowNetworkPlayers { get; set; } = true;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]

        public float LabelsMinZoom { get; set; } = 4f;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]

        public List<Marker> VehicleMarkers { get; } = new List<Marker>();
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public List<Marker> LocalPlayerMarkers { get; } = new List<Marker>();
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public List<Marker> NetworkPlayerMarkers { get; } = new List<Marker>();


        private Rectangle? pendingFocus;
        internal void FocusCells(Rectangle cells)
        {
            if (!Visible || Width < 80 || Height < 80) { pendingFocus = cells; return; }
            pendingFocus = null;
            Zoom = Math.Max(.05f, Math.Min(8f, Math.Min((Width - 40f) / Math.Max(1, cells.Width * CellPx), (Height - 40f) / Math.Max(1, cells.Height * CellPx))));
            Pan = new PointF(20 - cells.X * CellPx * Zoom, 20 - cells.Y * CellPx * Zoom);
            Invalidate();
        }

        public MapViewerControl()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            VisibleChanged += (_, _) => { if (pendingFocus is Rectangle cells) FocusCells(cells); };
            SizeChanged += (_, _) => { if (pendingFocus is Rectangle cells) FocusCells(cells); Invalidate(); };

            MouseWheel += (_, e) =>
            {
                var oldZoom = Zoom;
                var newZoom = Clamp(Zoom * (e.Delta > 0 ? 1.1f : 0.9f), 0.05f, 64f);


                var worldBefore = ScreenToWorld(e.Location, oldZoom, Pan);
                Zoom = newZoom;
                var worldAfter = ScreenToWorld(e.Location, Zoom, Pan);

                
                Pan = new PointF(
                    Pan.X + (worldAfter.X - worldBefore.X) * Zoom,
                    Pan.Y + (worldAfter.Y - worldBefore.Y) * Zoom
                );

                Invalidate();
            };

            MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    StartCellSelection(e.Location);
                    return;
                }

                if (e.Button != MouseButtons.Left) return;
                _panning = true;
                _panStartMouse = e.Location;
                _panStart = Pan;
                Cursor = Cursors.Hand;
            };

            MouseUp += (_, e) =>
            {
                if (e.Button == MouseButtons.Right && _selectingCells)
                {
                    UpdateCellSelection(e.Location, true);
                    _selectingCells = false;
                    Cursor = Cursors.Default;
                    return;
                }

                _panning = false;
                Cursor = Cursors.Default;
            };

            MouseMove += (_, e) =>
            {
                if (_selectingCells)
                {
                    UpdateCellSelection(e.Location, true);
                    return;
                }

                if (!_panning) return;
                var dx = e.X - _panStartMouse.X;
                var dy = e.Y - _panStartMouse.Y;
                Pan = new PointF(_panStart.X + dx, _panStart.Y + dy);
                Invalidate();
            };
        }

        private void StartCellSelection(Point screenPoint)
        {
            _selectingCells = true;
            _selectionStartCell = ScreenToCell(screenPoint);
            Cursor = Cursors.Cross;
            UpdateCellSelection(screenPoint, true);
        }

        private void UpdateCellSelection(Point screenPoint, bool notify)
        {
            var current = ScreenToCell(screenPoint);
            int x1 = Math.Min(_selectionStartCell.X, current.X);
            int y1 = Math.Min(_selectionStartCell.Y, current.Y);
            int x2 = Math.Max(_selectionStartCell.X, current.X);
            int y2 = Math.Max(_selectionStartCell.Y, current.Y);

            SelectedCellRect = new Rectangle(x1, y1, x2 - x1 + 1, y2 - y1 + 1);
            if (notify && CellSelectionChanged != null)
                CellSelectionChanged(x1, y1, x2, y2);

            Invalidate();
        }

        internal Point ScreenToCell(Point screenPoint)
        {
            var world = ScreenToWorld(screenPoint, Zoom, Pan);
            int cellX = (int)Math.Floor(world.X / CellPx);
            int cellY = (int)Math.Floor(world.Y / CellPx);
            return new Point(cellX, cellY);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            g.SmoothingMode = SmoothingMode.None;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            
            g.TranslateTransform(Pan.X, Pan.Y);
            g.ScaleTransform(Zoom, Zoom);

            
            if (BackgroundImage != null)
            {
                if (animating) ImageAnimator.UpdateFrames(BackgroundImage);
                float a = BackgroundAlpha;
                if (a >= 0.999f)
                {
                    g.DrawImage(BackgroundImage, BackgroundBounds);
                }
                else if (a > 0.001f)
                {
                    using var ia = new ImageAttributes();
                    var cm = new ColorMatrix
                    {
                        Matrix00 = 1,
                        Matrix11 = 1,
                        Matrix22 = 1,
                        Matrix33 = a,
                        Matrix44 = 1
                    };
                    ia.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

                    var dest = BackgroundBounds;
                    g.DrawImage(BackgroundImage,
                        new[] { new PointF(dest.Left, dest.Top), new PointF(dest.Right, dest.Top), new PointF(dest.Left, dest.Bottom) },
                        new RectangleF(0, 0, BackgroundImage.Width, BackgroundImage.Height), GraphicsUnit.Pixel, ia);
                }
                
            }


            
            var view = GetVisibleWorldRect();

            
            if (ShowMapChunks && MapChunks.Count > 0)
                DrawChunkSet(g, view, MapChunks, Color.FromArgb(110, Color.Lime));

            if (ShowIsoRegionChunks && IsoRegionChunks.Count > 0)
                DrawChunkSet(g, view, IsoRegionChunks, Color.FromArgb(90, Color.DeepSkyBlue));



            
            if (ShowChunkDataCells && ChunkDataCells.Count > 0)
                DrawCellSet(g, view, ChunkDataCells, Color.FromArgb(60, Color.Orange), Color.FromArgb(180, Color.Orange));

            if (ShowZPopCells && ZPopCells.Count > 0)
                DrawCellSet(g, view, ZPopCells, Color.FromArgb(45, Color.Red), Color.FromArgb(170, Color.Red));

            if (ShowAPopCells && APopCells.Count > 0)
                DrawCellSet(g, view, APopCells, Color.FromArgb(45, Color.Magenta), Color.FromArgb(170, Color.Magenta));

            if (ShowMetaCells && MetaCells.Count > 0)
                DrawCellSet(g, view, MetaCells, Color.FromArgb(40, Color.Yellow), Color.FromArgb(150, Color.Yellow));

            
            if (ShowGridCell)
                DrawGrid(g, view, CellPx, Color.FromArgb(180, 255, 255, 255), minSpacingPx: 8); 

            
            if (ShowGridChunk)
                DrawGrid(g, view, ChunkPx, Color.FromArgb(80, 255, 0, 0), minSpacingPx: 10);

            if (ShowCellCoords && Zoom >= CellCoordsMinZoom)
                DrawCellCoords(g, view);

            if (ShowBlamChunks && BlamChunks.Count > 0)
                DrawChunkSet(g, view, BlamChunks, Color.FromArgb(255, Color.Red));

            if (ShowVehicles) DrawMarkers(g, VehicleMarkers, Color.FromArgb(220, Color.Cyan));
            if (ShowLocalPlayers) DrawMarkers(g, LocalPlayerMarkers, Color.FromArgb(220, Color.Lime));
            if (ShowNetworkPlayers) DrawMarkers(g, NetworkPlayerMarkers, Color.FromArgb(220, Color.Yellow));


            
            if (SelectedCellRect.HasValue)
            {
                var r = SelectedCellRect.Value;
                var pxRect = new Rectangle(r.X * CellPx, r.Y * CellPx, r.Width * CellPx, r.Height * CellPx);
                using var pen = new Pen(Color.Cyan, 2f / Zoom); 
                g.DrawRectangle(pen, pxRect);
            }
        }

        private void DrawMarkers(Graphics g, List<Marker> markers, Color color)
        {
            if (markers.Count == 0) return;

            float r = 3f / Zoom;                 
            float penW = 1.5f / Zoom;

            using var b = new SolidBrush(color);
            using var p = new Pen(Color.Black, penW);

            foreach (var m in markers)
            {
                g.FillEllipse(b, m.Px - r, m.Py - r, 2 * r, 2 * r);
                g.DrawEllipse(p, m.Px - r, m.Py - r, 2 * r, 2 * r);

                if (Zoom >= LabelsMinZoom && !string.IsNullOrWhiteSpace(m.Label))
                {
                    using var font = new Font(FontFamily.GenericMonospace, 10f / Zoom, FontStyle.Regular, GraphicsUnit.World);
                    g.DrawString(m.Label, font, Brushes.Black, m.Px + (6f / Zoom), m.Py + (2f / Zoom));
                    g.DrawString(m.Label, font, Brushes.White, m.Px + (5f / Zoom), m.Py + (1f / Zoom));
                }
            }
        }


        private void DrawCellCoords(Graphics g, RectangleF view)
        {
            var (minCellX, minCellY, maxCellX, maxCellY) = VisibleCellBounds(view);

            using var brush = new SolidBrush(Color.FromArgb(200, Color.White));
            using var outline = new SolidBrush(Color.FromArgb(180, Color.Black));

            
            float fontSizeScreen = 14f; 
            float fontSizeWorld = fontSizeScreen / Zoom;

            using var font = new Font(FontFamily.GenericMonospace, fontSizeWorld, FontStyle.Regular, GraphicsUnit.World);

            
            float pad = 2f / Zoom;

            for (int cx = minCellX; cx <= maxCellX; cx++)
            {
                for (int cy = minCellY; cy <= maxCellY; cy++)
                {
                    float x = cx * CellPx + pad;
                    float y = cy * CellPx + pad;

                    string s = $"{cx},{cy}";

                    
                    g.DrawString(s, font, outline, x + (1f / Zoom), y);
                    g.DrawString(s, font, outline, x - (1f / Zoom), y);
                    g.DrawString(s, font, outline, x, y + (1f / Zoom));
                    g.DrawString(s, font, outline, x, y - (1f / Zoom));

                    g.DrawString(s, font, brush, x, y);
                }
            }
        }


        private void DrawChunkSet(Graphics g, RectangleF view, HashSet<(int x, int y)> set, Color fill)
        {
            var (minX, minY, maxX, maxY) = VisibleChunkBounds(view);

            using var brush = new SolidBrush(fill);

            
            foreach (var (cx, cy) in set)
            {
                if (cx < minX || cx > maxX || cy < minY || cy > maxY) continue;
                var r = new Rectangle(cx * ChunkPx, cy * ChunkPx, ChunkPx, ChunkPx);
                g.FillRectangle(brush, r);
            }
        }

        private void DrawCellSet(Graphics g, RectangleF view, HashSet<(int cx, int cy)> set, Color fill, Color border)
        {
            var (minCellX, minCellY, maxCellX, maxCellY) = VisibleCellBounds(view);

            using var brush = new SolidBrush(fill);
            using var pen = new Pen(border, 1f / Zoom);

            foreach (var (cx, cy) in set)
            {
                if (cx < minCellX || cx > maxCellX || cy < minCellY || cy > maxCellY) continue;
                var r = new Rectangle(cx * CellPx, cy * CellPx, CellPx, CellPx);
                g.FillRectangle(brush, r);
                g.DrawRectangle(pen, r);
            }
        }

        private void DrawGrid(Graphics g, RectangleF view, int stepPx, Color color, float minSpacingPx)
        {
            if (stepPx <= 0) return;
            if (stepPx * Zoom < minSpacingPx) return;

            using var pen = new Pen(color, 1f / Zoom);

            int x0 = (int)Math.Floor(view.Left / stepPx) * stepPx;
            int x1 = (int)Math.Ceiling(view.Right / stepPx) * stepPx;
            int y0 = (int)Math.Floor(view.Top / stepPx) * stepPx;
            int y1 = (int)Math.Ceiling(view.Bottom / stepPx) * stepPx;

            for (int x = x0; x <= x1; x += stepPx)
                g.DrawLine(pen, x, view.Top, x, view.Bottom);

            for (int y = y0; y <= y1; y += stepPx)
                g.DrawLine(pen, view.Left, y, view.Right, y);
        }

        private RectangleF GetVisibleWorldRect()
        {
            var tl = ScreenToWorld(new Point(0, 0), Zoom, Pan);
            var br = ScreenToWorld(new Point(Width, Height), Zoom, Pan);
            return RectangleF.FromLTRB(tl.X, tl.Y, br.X, br.Y);
        }

        private static PointF ScreenToWorld(Point p, float zoom, PointF pan)
            => new PointF((p.X - pan.X) / zoom, (p.Y - pan.Y) / zoom);

        private (int minX, int minY, int maxX, int maxY) VisibleChunkBounds(RectangleF view)
        {
            int minX = (int)Math.Floor(view.Left / ChunkPx);
            int minY = (int)Math.Floor(view.Top / ChunkPx);
            int maxX = (int)Math.Ceiling(view.Right / ChunkPx);
            int maxY = (int)Math.Ceiling(view.Bottom / ChunkPx);
            return (minX, minY, maxX, maxY);
        }

        private (int minX, int minY, int maxX, int maxY) VisibleCellBounds(RectangleF view)
        {
            int minX = (int)Math.Floor(view.Left / CellPx);
            int minY = (int)Math.Floor(view.Top / CellPx);
            int maxX = (int)Math.Ceiling(view.Right / CellPx);
            int maxY = (int)Math.Ceiling(view.Bottom / CellPx);
            return (minX, minY, maxX, maxY);
        }
    }
}

