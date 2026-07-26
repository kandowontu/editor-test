using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Controls;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        private bool firstPerson3DEnabled = false;
        private bool firstPerson3DInitialized = false;
        private long firstPerson3DLastRebuildMs = -1;
        private bool firstPerson3DHasMesh = false;
        private int firstPerson3DLastMinX = int.MinValue;
        private int firstPerson3DLastMaxX = int.MinValue;
        private int firstPerson3DLastMinY = int.MinValue;
        private int firstPerson3DLastMaxY = int.MinValue;
        private Color firstPerson3DLastBackgroundTint;
        private Color firstPerson3DLastTileTint;
        private Color firstPerson3DLastGroundTint;
        private bool firstPerson3DLastForceSolidBlack;

        private OrthographicCamera? firstPerson3DCamera;
        private ModelVisual3D? firstPerson3DVisual;
        private Model3DGroup? firstPerson3DRoot;
        private Model3DGroup? firstPerson3DDynamic;
        private GeometryModel3D? firstPerson3DFloorModel;
        private GeometryModel3D? firstPerson3DWallModel;
        private GeometryModel3D? firstPerson3DSlabModel;
        private GeometryModel3D? firstPerson3DHazardModel;
        private Model3DGroup? firstPerson3DTileArtGroup;
        private Model3DGroup? firstPerson3DSpriteGroup;
        private Model3DGroup? firstPerson3DInteractiveGroup;
        private Model3DGroup? firstPerson3DPlayerGroup;
        private Model3DGroup? firstPerson3DPlayer2Group;
        private Transform3DGroup? firstPerson3DPlayerTransformGroup;
        private Transform3DGroup? firstPerson3DPlayer2TransformGroup;
        private TranslateTransform3D? firstPerson3DPlayerTransform;
        private TranslateTransform3D? firstPerson3DPlayer2Transform;
        private AxisAngleRotation3D? firstPerson3DPlayerRotation;
        private AxisAngleRotation3D? firstPerson3DPlayer2Rotation;
        private ScaleTransform3D? firstPerson3DPlayerGravityTransform;
        private ScaleTransform3D? firstPerson3DPlayer2GravityTransform;
        private int firstPerson3DPlayerMode = -1;
        private bool firstPerson3DPlayerMini;
        private int firstPerson3DPlayer2Mode = -1;
        private bool firstPerson3DPlayer2Mini;

        private DiffuseMaterial? firstPerson3DWallMaterial;
        private DiffuseMaterial? firstPerson3DSlabMaterial;
        private MaterialGroup? firstPerson3DHazardMaterial;
        private DiffuseMaterial? firstPerson3DFloorMaterial;
        private MaterialGroup? firstPerson3DPlayerMaterial;
        private MaterialGroup? firstPerson3DPlayerAccentMaterial;
        private MaterialGroup? firstPerson3DPlayer2Material;
        private readonly Dictionary<int, MaterialGroup> firstPerson3DMarkerMaterials = new Dictionary<int, MaterialGroup>();
        private readonly Dictionary<ImageSource, Material> firstPerson3DTextureMaterials = new Dictionary<ImageSource, Material>();
        private readonly Dictionary<ImageSource, bool[]> firstPerson3DAlphaMasks = new Dictionary<ImageSource, bool[]>();
        private readonly Dictionary<int, MaterialGroup> firstPerson3DFallbackMaterials = new Dictionary<int, MaterialGroup>();
        private readonly List<FirstPerson3DInteractiveInstance> firstPerson3DInteractiveInstances =
            new List<FirstPerson3DInteractiveInstance>();

        private sealed class FirstPerson3DInteractiveInstance
        {
            public AxisAngleRotation3D Spin { get; }
            public ScaleTransform3D Pulse { get; }
            public int Kind { get; }
            public double Phase { get; }

            public FirstPerson3DInteractiveInstance(
                AxisAngleRotation3D spin,
                ScaleTransform3D pulse,
                int kind,
                double phase)
            {
                Spin = spin;
                Pulse = pulse;
                Kind = kind;
                Phase = phase;
            }
        }

        private bool firstPerson3DRotateActive = false;
        private bool firstPerson3DPanActive = false;
        private Point firstPerson3DLastMouse;
        private double firstPerson3DPanX = 0.0;
        private double firstPerson3DPanY = 0.0;
        private double firstPerson3DYawDeg = -135.0;
        private double firstPerson3DPitchDeg = -31.0;
        private const double FirstPersonOrbitDistance = 19.5;

        private const double FirstPersonZoomMin = 45.0;
        private const double FirstPersonZoomMax = 110.0;
        private const double FirstPersonZoomNeutral = (FirstPersonZoomMin + FirstPersonZoomMax) * 0.5;
        private double firstPersonZoomValue = FirstPersonZoomNeutral;

        private const int FirstPerson3DRebuildIntervalMs = 90;
        private const int FirstPersonNearTiles = 14;
        private const int FirstPersonFarTiles = 14;
        private const int FirstPersonUpTiles = 10;
        private const int FirstPersonDownTiles = 10;
        private const int FirstPersonCollisionVoxelGrid = 4;
        private const int FirstPersonArtworkMaskGrid = 16;

        private void FirstPersonZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            firstPersonZoomValue = Math.Max(FirstPersonZoomMin, Math.Min(FirstPersonZoomMax, e.NewValue));
            if (firstPerson3DCamera != null)
            {
                firstPerson3DCamera.Width = ComputeIsometricWidth(firstPersonZoomValue);
            }

            ApplyZoomForCurrentViewMode();
        }

        private static double ComputeIsometricWidth(double zoomValue)
        {
            double t = (zoomValue - FirstPersonZoomMin) / (FirstPersonZoomMax - FirstPersonZoomMin);
            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;
            return 8.0 + (t * 24.0);
        }

        private static double Compute2DZoomScale(double zoomValue)
        {
            // Piecewise mapping centered at slider midpoint:
            // lower values zoom in strongly, higher values zoom out meaningfully.
            if (zoomValue >= FirstPersonZoomNeutral)
            {
                double t = (zoomValue - FirstPersonZoomNeutral) / (FirstPersonZoomMax - FirstPersonZoomNeutral);
                return 1.0 - (t * 0.65); // 1.00 -> 0.35
            }

            {
                double t = (FirstPersonZoomNeutral - zoomValue) / (FirstPersonZoomNeutral - FirstPersonZoomMin);
                return 1.0 + (t * 1.5); // 1.00 -> 2.50
            }
        }

        private void ApplyZoomForCurrentViewMode()
        {
            if (firstPerson3DEnabled) return;

            double zoomScale = Compute2DZoomScale(firstPersonZoomValue);
            sim2DZoomScale = zoomScale;

            int dispScale = Math.Max(1, _simulatorDisplayScale);

            try
            {
                // The canvas lives in world coords (NES pixels).  When zoomed out we expand it so
                // more tiles are visible; LayoutTransform then shrinks it back to fit the viewport.
                // The viewport itself is already sized to NES*dispScale by the constructor, so the
                // combined scale we apply to RenderCanvas must be zoomScale * dispScale so the
                // content exactly fills the GameViewportSurface at every display-scale setting.
                double totalScale = zoomScale * dispScale;
                double newW = NES_W * TILE / zoomScale;   // world-coord canvas width
                double newH = NES_H * TILE / zoomScale;   // world-coord canvas height

                if (RenderCanvas != null)
                {
                    RenderCanvas.Width  = newW;
                    RenderCanvas.Height = newH;
                    RenderCanvas.LayoutTransform = new ScaleTransform(totalScale, totalScale);

                    // Keep background rectangle filling the whole (expanded) canvas.
                    if (bgRectPersistent != null)
                    {
                        bgRectPersistent.Width  = newW;
                        bgRectPersistent.Height = newH;
                    }
                }

                // GameViewportSurface is already sized by the constructor; do not add another
                // LayoutTransform on it or the overlays — that would double-scale them.
                if (GameViewportSurface != null) GameViewportSurface.LayoutTransform = null;
                if (PauseOverlay != null)        PauseOverlay.LayoutTransform        = null;
                if (LevelCompleteOverlay != null) LevelCompleteOverlay.LayoutTransform = null;

                // Force tile-cache rebuild at new coverage size.
                tileLayerCache  = null;
                cachedStartTileX = int.MinValue;
            }
            catch { }
        }

        private void FirstPerson3DCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            firstPerson3DEnabled = FirstPerson3DCheckBox?.IsChecked == true;
            if (firstPerson3DEnabled)
            {
                EnsureFirstPerson3DInitialized();
                if (RenderCanvas != null) RenderCanvas.Visibility = Visibility.Collapsed;
                if (FirstPersonBackdrop != null) FirstPersonBackdrop.Visibility = Visibility.Visible;
                if (FirstPersonViewport != null) FirstPersonViewport.Visibility = Visibility.Visible;
                if (FirstPersonHintText != null) FirstPersonHintText.Visibility = Visibility.Visible;
                UpdateFirstPerson3D(force: true);
            }
            else
            {
                firstPerson3DRotateActive = false;
                firstPerson3DPanActive = false;
                if (FirstPersonViewport != null)
                {
                    try { FirstPersonViewport.ReleaseMouseCapture(); } catch { }
                    FirstPersonViewport.Visibility = Visibility.Collapsed;
                }
                if (FirstPersonBackdrop != null) FirstPersonBackdrop.Visibility = Visibility.Collapsed;
                if (FirstPersonHintText != null) FirstPersonHintText.Visibility = Visibility.Collapsed;
                if (RenderCanvas != null) RenderCanvas.Visibility = Visibility.Visible;

                // Re-apply slider zoom to 2D renderer when leaving FP mode.
                ApplyZoomForCurrentViewMode();
            }
        }

        private void FirstPersonViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!firstPerson3DEnabled || FirstPersonViewport == null) return;
            firstPerson3DRotateActive = true;
            firstPerson3DPanActive = false;
            firstPerson3DLastMouse = e.GetPosition(FirstPersonViewport);
            try { FirstPersonViewport.CaptureMouse(); } catch { }
            e.Handled = true;
        }

        private void FirstPersonViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!firstPerson3DEnabled || FirstPersonViewport == null) return;
            firstPerson3DRotateActive = false;
            if (!firstPerson3DPanActive)
            {
                try { FirstPersonViewport.ReleaseMouseCapture(); } catch { }
            }
            e.Handled = true;
        }

        private void FirstPersonViewport_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!firstPerson3DEnabled || FirstPersonViewport == null) return;
            firstPerson3DPanActive = true;
            firstPerson3DRotateActive = false;
            firstPerson3DLastMouse = e.GetPosition(FirstPersonViewport);
            try { FirstPersonViewport.CaptureMouse(); } catch { }
            e.Handled = true;
        }

        private void FirstPersonViewport_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!firstPerson3DEnabled || FirstPersonViewport == null) return;
            firstPerson3DPanActive = false;
            if (!firstPerson3DRotateActive)
            {
                try { FirstPersonViewport.ReleaseMouseCapture(); } catch { }
            }
            e.Handled = true;
        }

        private void FirstPersonViewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!firstPerson3DEnabled || FirstPersonViewport == null) return;

            // Recover gracefully if the initial down event was missed.
            if (!firstPerson3DRotateActive && !firstPerson3DPanActive)
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    firstPerson3DRotateActive = true;
                    firstPerson3DPanActive = false;
                    firstPerson3DLastMouse = e.GetPosition(FirstPersonViewport);
                    try { FirstPersonViewport.CaptureMouse(); } catch { }
                }
                else if (e.RightButton == MouseButtonState.Pressed)
                {
                    firstPerson3DPanActive = true;
                    firstPerson3DRotateActive = false;
                    firstPerson3DLastMouse = e.GetPosition(FirstPersonViewport);
                    try { FirstPersonViewport.CaptureMouse(); } catch { }
                }
                else
                {
                    return;
                }
            }

            bool rotateHeld = e.LeftButton == MouseButtonState.Pressed;
            bool panHeld = e.RightButton == MouseButtonState.Pressed;

            if (firstPerson3DRotateActive && !rotateHeld)
            {
                firstPerson3DRotateActive = false;
            }
            if (firstPerson3DPanActive && !panHeld)
            {
                firstPerson3DPanActive = false;
            }
            if (!firstPerson3DRotateActive && !firstPerson3DPanActive)
            {
                try { FirstPersonViewport.ReleaseMouseCapture(); } catch { }
                return;
            }

            Point now = e.GetPosition(FirstPersonViewport);
            double dx = now.X - firstPerson3DLastMouse.X;
            double dy = now.Y - firstPerson3DLastMouse.Y;
            firstPerson3DLastMouse = now;

            if (firstPerson3DRotateActive)
            {
                // Left-drag rotates camera around the target.
                firstPerson3DYawDeg += dx * 0.32;
                firstPerson3DPitchDeg += dy * 0.22;
                firstPerson3DYawDeg = NormalizeOrbitAngle(firstPerson3DYawDeg);
                firstPerson3DPitchDeg = NormalizeOrbitAngle(firstPerson3DPitchDeg);
            }
            else if (firstPerson3DPanActive)
            {
                // Right-drag pans target in world-space.
                double worldPerPixel = ComputeIsometricWidth(firstPersonZoomValue) / 256.0;
                firstPerson3DPanX -= dx * worldPerPixel * 0.95;
                firstPerson3DPanY += dy * worldPerPixel * 0.95;
            }

            UpdateFirstPerson3D(force: false);
            e.Handled = true;
        }

        private void EnsureFirstPerson3DInitialized()
        {
            if (firstPerson3DInitialized) return;
            if (FirstPersonViewport == null) return;

            firstPerson3DWallMaterial = CreateMaterial(Color.FromRgb(88, 122, 178));
            firstPerson3DSlabMaterial = CreateMaterial(Color.FromRgb(56, 87, 137));
            firstPerson3DFloorMaterial = CreateMaterial(Color.FromRgb(28, 31, 40));

            var hazardDiffuse = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(230, 95, 55)));
            var hazardEmissive = new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(175, 255, 120, 80)));
            if (hazardDiffuse.CanFreeze) hazardDiffuse.Freeze();
            if (hazardEmissive.CanFreeze) hazardEmissive.Freeze();
            firstPerson3DHazardMaterial = new MaterialGroup();
            firstPerson3DHazardMaterial.Children.Add(hazardDiffuse);
            firstPerson3DHazardMaterial.Children.Add(hazardEmissive);

            Color p1Color = playerTintEnabled ? playerTint : Color.FromRgb(114, 255, 132);
            firstPerson3DPlayerMaterial = CreateLitMaterial(p1Color, 0.24);
            firstPerson3DPlayerAccentMaterial = CreateLitMaterial(Color.FromRgb(245, 255, 255), 0.18);
            firstPerson3DPlayer2Material = CreateLitMaterial(Color.FromRgb(94, 210, 255), 0.24);

            firstPerson3DCamera = new OrthographicCamera
            {
                Width = ComputeIsometricWidth(firstPersonZoomValue),
                Position = new Point3D(0, 0, -0.25),
                LookDirection = new Vector3D(1.7, 0.0, 0.0),
                UpDirection = new Vector3D(0, 1, 0),
                NearPlaneDistance = 0.05,
                FarPlaneDistance = 120
            };

            try
            {
                if (FirstPersonZoomSlider != null)
                {
                    firstPersonZoomValue = Math.Max(45.0, Math.Min(110.0, FirstPersonZoomSlider.Value));
                    firstPerson3DCamera.Width = ComputeIsometricWidth(firstPersonZoomValue);
                }
            }
            catch { }

            firstPerson3DRoot = new Model3DGroup();
            firstPerson3DDynamic = new Model3DGroup();
            firstPerson3DRoot.Children.Add(new AmbientLight(Color.FromRgb(76, 86, 112)));
            firstPerson3DRoot.Children.Add(new DirectionalLight(Color.FromRgb(238, 244, 255), new Vector3D(-0.35, -0.72, -0.46)));
            firstPerson3DRoot.Children.Add(new DirectionalLight(Color.FromRgb(86, 124, 184), new Vector3D(0.55, 0.25, 0.75)));
            firstPerson3DRoot.Children.Add(firstPerson3DDynamic);

            firstPerson3DVisual = new ModelVisual3D { Content = firstPerson3DRoot };

            firstPerson3DFloorModel = new GeometryModel3D();
            firstPerson3DWallModel = new GeometryModel3D();
            firstPerson3DSlabModel = new GeometryModel3D();
            firstPerson3DHazardModel = new GeometryModel3D();
            firstPerson3DTileArtGroup = new Model3DGroup();
            firstPerson3DSpriteGroup = new Model3DGroup();
            firstPerson3DInteractiveGroup = new Model3DGroup();
            firstPerson3DPlayerGroup = new Model3DGroup();
            firstPerson3DPlayer2Group = new Model3DGroup();
            firstPerson3DPlayerTransform = new TranslateTransform3D();
            firstPerson3DPlayer2Transform = new TranslateTransform3D();
            firstPerson3DPlayerRotation = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
            firstPerson3DPlayer2Rotation = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
            firstPerson3DPlayerGravityTransform = new ScaleTransform3D(1, 1, 1, 0, 0.5, 0);
            firstPerson3DPlayer2GravityTransform = new ScaleTransform3D(1, 1, 1, 0, 0.5, 0);
            firstPerson3DPlayerTransformGroup = new Transform3DGroup();
            firstPerson3DPlayerTransformGroup.Children.Add(firstPerson3DPlayerGravityTransform);
            firstPerson3DPlayerTransformGroup.Children.Add(
                new RotateTransform3D(firstPerson3DPlayerRotation, 0.0, 0.5, 0));
            firstPerson3DPlayerTransformGroup.Children.Add(firstPerson3DPlayerTransform);
            firstPerson3DPlayer2TransformGroup = new Transform3DGroup();
            firstPerson3DPlayer2TransformGroup.Children.Add(firstPerson3DPlayer2GravityTransform);
            firstPerson3DPlayer2TransformGroup.Children.Add(
                new RotateTransform3D(firstPerson3DPlayer2Rotation, 0.0, 0.5, 0));
            firstPerson3DPlayer2TransformGroup.Children.Add(firstPerson3DPlayer2Transform);

            firstPerson3DFloorModel.Material = firstPerson3DFloorMaterial;
            firstPerson3DFloorModel.BackMaterial = firstPerson3DFloorMaterial;
            firstPerson3DWallModel.Material = firstPerson3DWallMaterial;
            firstPerson3DWallModel.BackMaterial = firstPerson3DWallMaterial;
            firstPerson3DSlabModel.Material = firstPerson3DSlabMaterial;
            firstPerson3DSlabModel.BackMaterial = firstPerson3DSlabMaterial;
            firstPerson3DHazardModel.Material = firstPerson3DHazardMaterial;
            firstPerson3DHazardModel.BackMaterial = firstPerson3DHazardMaterial;

            firstPerson3DPlayerGroup.Transform = firstPerson3DPlayerTransformGroup;
            firstPerson3DPlayer2Group.Transform = firstPerson3DPlayer2TransformGroup;
            RebuildFirstPersonPlayerModel(firstPerson3DPlayerGroup, currentGameMode, miniMode, playerTwo: false);
            firstPerson3DPlayerMode = currentGameMode;
            firstPerson3DPlayerMini = miniMode;

            firstPerson3DDynamic.Children.Add(firstPerson3DFloorModel);
            firstPerson3DDynamic.Children.Add(firstPerson3DWallModel);
            firstPerson3DDynamic.Children.Add(firstPerson3DSlabModel);
            firstPerson3DDynamic.Children.Add(firstPerson3DHazardModel);
            firstPerson3DDynamic.Children.Add(firstPerson3DTileArtGroup);
            firstPerson3DDynamic.Children.Add(firstPerson3DSpriteGroup);
            firstPerson3DDynamic.Children.Add(firstPerson3DInteractiveGroup);
            firstPerson3DDynamic.Children.Add(firstPerson3DPlayerGroup);
            firstPerson3DDynamic.Children.Add(firstPerson3DPlayer2Group);

            FirstPersonViewport.Camera = firstPerson3DCamera;
            FirstPersonViewport.Children.Clear();
            FirstPersonViewport.Children.Add(firstPerson3DVisual);
            RenderOptions.SetBitmapScalingMode(FirstPersonViewport, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(FirstPersonViewport, EdgeMode.Aliased);

            firstPerson3DInitialized = true;
        }

        private void UpdateFirstPerson3D(bool force = false)
        {
            if (!firstPerson3DEnabled) return;
            if (windowClosed) return;
            EnsureFirstPerson3DInitialized();
            if (!firstPerson3DInitialized || firstPerson3DCamera == null) return;

            int playerXpx = playerX_fixed >> 8;
            int playerYpx = playerY_fixed >> 8;
            int playerTileX = playerXpx / TILE;
            int playerTileY = Math.Max(0, playerYpx / TILE);

            double playerWorldX = (playerXpx + (TILE / 2.0)) / TILE;
            double playerFeetWorldY = -((playerYpx + playerVisualHeight) / (double)TILE);

            // Isometric top-right view with drag panning offsets.
            double targetX = playerWorldX + firstPerson3DPanX;
            double targetY = playerFeetWorldY + 0.7 + firstPerson3DPanY;
            double targetZ = 0.0;
            double yawRad = firstPerson3DYawDeg * Math.PI / 180.0;
            double pitchRad = firstPerson3DPitchDeg * Math.PI / 180.0;

            double lookX = Math.Cos(pitchRad) * Math.Cos(yawRad);
            double lookY = Math.Sin(pitchRad);
            double lookZ = Math.Cos(pitchRad) * Math.Sin(yawRad);
            var lookDir = new Vector3D(lookX, lookY, lookZ);
            lookDir.Normalize();
            // Use the spherical-coordinate tangent as camera-up. A fixed world-up
            // vector becomes parallel to LookDirection at the poles and is the
            // reason vertical orbiting previously had to be clamped.
            var cameraUp = new Vector3D(
                -Math.Sin(pitchRad) * Math.Cos(yawRad),
                Math.Cos(pitchRad),
                -Math.Sin(pitchRad) * Math.Sin(yawRad));
            cameraUp.Normalize();

            firstPerson3DCamera.Position = new Point3D(
                targetX - (lookDir.X * FirstPersonOrbitDistance),
                targetY - (lookDir.Y * FirstPersonOrbitDistance),
                targetZ - (lookDir.Z * FirstPersonOrbitDistance));
            firstPerson3DCamera.LookDirection = new Vector3D(lookDir.X * FirstPersonOrbitDistance, lookDir.Y * FirstPersonOrbitDistance, lookDir.Z * FirstPersonOrbitDistance);
            firstPerson3DCamera.UpDirection = cameraUp;
            firstPerson3DCamera.Width = ComputeIsometricWidth(firstPersonZoomValue);

            if (firstPerson3DPlayerTransform != null)
            {
                firstPerson3DPlayerTransform.OffsetX = playerWorldX;
                firstPerson3DPlayerTransform.OffsetY = playerFeetWorldY;
                firstPerson3DPlayerTransform.OffsetZ = 1.02;
            }
            if (firstPerson3DPlayerRotation != null)
            {
                firstPerson3DPlayerRotation.Angle = GetFirstPersonPlayerAngle(currentGameMode, 0);
            }
            if (firstPerson3DPlayerGravityTransform != null)
            {
                firstPerson3DPlayerGravityTransform.ScaleY = gravityReversed ? -1.0 : 1.0;
            }
            if (firstPerson3DPlayerGroup != null &&
                (firstPerson3DPlayerMode != currentGameMode || firstPerson3DPlayerMini != miniMode))
            {
                RebuildFirstPersonPlayerModel(firstPerson3DPlayerGroup, currentGameMode, miniMode, playerTwo: false);
                firstPerson3DPlayerMode = currentGameMode;
                firstPerson3DPlayerMini = miniMode;
            }

            if (firstPerson3DPlayer2Group != null)
            {
                if (dual)
                {
                    bool p2Mini = player_mini.Length > 1 && player_mini[1];
                    if (firstPerson3DPlayer2Group.Children.Count == 0 ||
                        firstPerson3DPlayer2Mode != currentGameMode ||
                        firstPerson3DPlayer2Mini != p2Mini)
                    {
                        RebuildFirstPersonPlayerModel(firstPerson3DPlayer2Group, currentGameMode, p2Mini, playerTwo: true);
                        firstPerson3DPlayer2Mode = currentGameMode;
                        firstPerson3DPlayer2Mini = p2Mini;
                    }

                    int p2Xpx = player_x_fixed.Length > 1 ? player_x_fixed[1] >> 8 : playerXpx;
                    int p2Ypx = player_y_fixed.Length > 1 ? player_y_fixed[1] >> 8 : playerYpx;
                    int p2Height = p2Mini ? Math.Max(8, TILE / 2) : TILE;
                    if (firstPerson3DPlayer2Transform != null)
                    {
                        firstPerson3DPlayer2Transform.OffsetX = (p2Xpx + (TILE / 2.0)) / TILE;
                        firstPerson3DPlayer2Transform.OffsetY = -((p2Ypx + p2Height) / (double)TILE);
                        firstPerson3DPlayer2Transform.OffsetZ = 0.82;
                    }
                    if (firstPerson3DPlayer2Rotation != null)
                    {
                        firstPerson3DPlayer2Rotation.Angle = GetFirstPersonPlayerAngle(currentGameMode, 1);
                    }
                    if (firstPerson3DPlayer2GravityTransform != null)
                    {
                        bool p2GravityReversed = player_gravity.Length > 1 && player_gravity[1] != 0;
                        firstPerson3DPlayer2GravityTransform.ScaleY = p2GravityReversed ? -1.0 : 1.0;
                    }
                }
                else if (firstPerson3DPlayer2Group.Children.Count > 0)
                {
                    firstPerson3DPlayer2Group.Children.Clear();
                    firstPerson3DPlayer2Mode = -1;
                }
            }

            UpdateFirstPersonInteractiveAnimations();

            if (!ShouldRebuildFirstPersonGeometry(playerTileX, playerTileY, force))
            {
                return;
            }

            BuildFirstPersonGeometry(playerTileX, playerTileY);
        }

        private static double NormalizeOrbitAngle(double angle)
        {
            angle %= 360.0;
            if (angle > 180.0) angle -= 360.0;
            if (angle <= -180.0) angle += 360.0;
            return angle;
        }

        private bool ShouldRebuildFirstPersonGeometry(int playerTileX, int playerTileY, bool force)
        {
            if (force || !firstPerson3DHasMesh) return true;
            if (firstPerson3DLastBackgroundTint != backgroundTint ||
                firstPerson3DLastTileTint != tileTint ||
                firstPerson3DLastGroundTint != groundTint ||
                firstPerson3DLastForceSolidBlack != backgroundForceSolidBlack)
                return true;

            int minX = Math.Max(0, playerTileX - FirstPersonNearTiles);
            int maxX = Math.Min(mapWidth - 1, playerTileX + FirstPersonFarTiles);
            int minY = Math.Max(0, playerTileY - FirstPersonUpTiles);
            int maxY = Math.Min(mapHeight - 1, playerTileY + FirstPersonDownTiles);

            if (minX != firstPerson3DLastMinX || maxX != firstPerson3DLastMaxX || minY != firstPerson3DLastMinY || maxY != firstPerson3DLastMaxY)
            {
                long now = Environment.TickCount64;
                if (firstPerson3DLastRebuildMs < 0 || (now - firstPerson3DLastRebuildMs) >= FirstPerson3DRebuildIntervalMs)
                {
                    return true;
                }
            }

            return false;
        }

        private void BuildFirstPersonGeometry(int playerTileX, int playerTileY)
        {
            if (firstPerson3DWallModel == null || firstPerson3DSlabModel == null ||
                firstPerson3DHazardModel == null || firstPerson3DFloorModel == null ||
                firstPerson3DTileArtGroup == null || firstPerson3DSpriteGroup == null ||
                firstPerson3DInteractiveGroup == null) return;

            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;

            int minX = Math.Max(0, playerTileX - FirstPersonNearTiles);
            int maxX = Math.Min(mapWidth - 1, playerTileX + FirstPersonFarTiles);
            int minY = Math.Max(0, playerTileY - FirstPersonUpTiles);
            int maxY = Math.Min(mapHeight - 1, playerTileY + FirstPersonDownTiles);
            UpdateFirstPersonScenePalette();

            var wallMesh = new MeshGeometry3D();
            var slabMesh = new MeshGeometry3D();
            var hazardMesh = new MeshGeometry3D();
            var floorMesh = new MeshGeometry3D();
            var tileArtMeshes = new Dictionary<int, MeshGeometry3D>();
            var tileArtSources = new Dictionary<int, ImageSource?>();
            var spriteArtMeshes = new Dictionary<int, MeshGeometry3D>();
            var spriteArtSources = new Dictionary<int, ImageSource?>();
            firstPerson3DInteractiveGroup.Children.Clear();
            firstPerson3DInteractiveInstances.Clear();
            if (firstPerson3DTextureMaterials.Count > 512)
            {
                firstPerson3DTextureMaterials.Clear();
                firstPerson3DAlphaMasks.Clear();
            }

            for (int tyWorld = minY; tyWorld <= maxY; tyWorld++)
            {
                int tyArray = tyWorld + groundRowsToReserve;
                if (tyArray < 0 || tyArray >= mapHeight) continue;

                for (int tx = minX; tx <= maxX; tx++)
                {
                    int idx = tyArray * mapWidth + tx;
                    if (idx < 0 || idx >= tiles.Length) continue;

                    int tid = tiles[idx];
                    if (tid < 0) continue;
                    int visualTid = ResolveFirstPersonTileIndex(tid);
                    var col = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(tid));

                    double x0 = tx;
                    double x1 = tx + 1.0;
                    double yTop = -tyWorld;
                    double yBottom = yTop - 1.0;

                    ImageSource? tileSource;
                    if (!tileArtMeshes.TryGetValue(visualTid, out MeshGeometry3D? artMesh))
                    {
                        artMesh = new MeshGeometry3D();
                        tileArtMeshes[visualTid] = artMesh;
                        tileSource = GetFirstPersonTileImage(visualTid, tid);
                        tileArtSources[visualTid] = tileSource;
                    }
                    else
                    {
                        tileArtSources.TryGetValue(visualTid, out tileSource);
                    }

                    if (tileSource != null)
                    {
                        AddTexturedArtworkGeometry(
                            artMesh,
                            GetFirstPersonAlphaMask(tileSource),
                            x0, x1, yBottom, yTop,
                            -0.62, 0.62);
                    }
                    else if (col != MetatileCollision.COL_NONE &&
                        !IsPureDeathHazardCollision(col) &&
                        !IsSpikeStyleCollision(col))
                    {
                        // Collision-derived geometry is a last-resort placeholder
                        // for non-hazard solids only. A missing hazard image is
                        // never permission to invent visible spikes.
                        AddCollisionTileGeometry(
                            col, x0, x1, yBottom, yTop,
                            wallMesh, slabMesh, hazardMesh);
                    }
                }
            }

            // Interactive families use purpose-built volumetric models. Every other
            // sprite keeps its exact editor artwork on a shallow extrusion so
            // decorations and non-color triggers never disappear from the scene.
            foreach (int sidx in nonEmptySpriteIndices)
            {
                if (sidx < 0 || sidx >= sprites.Length) continue;
                int rawSid = sprites[sidx];
                if (rawSid < 0) continue;
                int sid = rawSid & 0xFF;
                if (IsFirstPerson3DInvisibleSprite(sid)) continue;

                bool isOrb = SharedPhysics.IsOrbSprite(sid) || SharedPhysics.IsSpiderOrb(sid) ||
                    SharedPhysics.IsDashOrb(sid) || sid == 0x79;
                bool isPortal = IsPortalSpriteSid(sid);
                bool isCoin = SharedPhysics.IsCoinSprite(sid) || SharedPhysics.IsMiniCoinSprite(sid);
                bool isPad = SharedPhysics.IsAnyPad(sid) || SharedPhysics.IsSpiderPad(sid);

                int sx = sidx % mapWidth;
                int syArray = sidx / mapWidth;
                int syWorld = syArray - groundRowsToReserve;

                if (sx < minX - 1 || sx > maxX + 1 || syWorld < minY - 1 || syWorld > maxY + 1) continue;

                int pxOff = 0;
                int pyOff = 0;
                if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(sidx, out var offsets))
                {
                    pxOff = offsets.offsetX;
                    pyOff = offsets.offsetY;
                }
                else if (spriteAnchors != null && spriteAnchors.TryGetValue(sidx, out var anchor))
                {
                    int anchorKey = anchor.anchorTileY * mapWidth + anchor.anchorTileX;
                    if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(anchorKey, out var anchorOffsets))
                    {
                        pxOff = anchorOffsets.offsetX;
                        pyOff = anchorOffsets.offsetY;
                    }
                }

                ImageSource? spriteSource = GetFirstPersonSpriteImage(sid);
                int imageWidth = TILE;
                int imageHeight = TILE;
                if (spriteSource is BitmapSource bitmap)
                {
                    imageWidth = Math.Max(1, bitmap.PixelWidth);
                    imageHeight = Math.Max(1, bitmap.PixelHeight);
                }

                double visualWidthTiles = imageWidth / (double)TILE;
                double visualHeightTiles = imageHeight / (double)TILE;
                if (isPortal)
                {
                    GetFirstPersonPortalFootprint(
                        sid, out visualWidthTiles, out visualHeightTiles);
                }

                double left = sx + (pxOff / (double)TILE);
                double top = -(syWorld + (pyOff / (double)TILE));
                double right = left + visualWidthTiles;
                double bottom = top - visualHeightTiles;

                if (isOrb || isPortal || isCoin || isPad)
                {
                    AddFirstPersonInteractiveModel(
                        sid, isPortal, isCoin, isPad,
                        left, right, bottom, top, sidx);
                }
                else
                {
                    int artKey = sid;
                    if (!spriteArtMeshes.TryGetValue(artKey, out MeshGeometry3D? spriteMesh))
                    {
                        spriteMesh = new MeshGeometry3D();
                        spriteArtMeshes[artKey] = spriteMesh;
                        spriteArtSources[artKey] = spriteSource;
                    }
                    AddTexturedArtworkGeometry(
                        spriteMesh,
                        GetFirstPersonAlphaMask(spriteSource),
                        left, right, bottom, top,
                        0.68, 0.80);
                }
            }

            // Floor sits at the bottom of the rendered tile window and uses a real slab
            // so it stays visually attached when the camera rotates and exposes depth.
            double floorY = -(maxY + 1.0) + 3.0;
            AddBox(floorMesh,
                minX - 2.0,
                floorY - 1.25,
                -8.0,
                maxX + 3.0,
                floorY,
                8.0);

            firstPerson3DFloorModel.Geometry = floorMesh;
            firstPerson3DWallModel.Geometry = wallMesh;
            firstPerson3DSlabModel.Geometry = slabMesh;
            firstPerson3DHazardModel.Geometry = hazardMesh;

            firstPerson3DTileArtGroup.Children.Clear();
            foreach (var kvp in tileArtMeshes)
            {
                if (kvp.Value.Positions.Count == 0) continue;
                Material material = tileArtSources.TryGetValue(kvp.Key, out ImageSource? source) && source != null
                    ? GetFirstPersonTextureMaterial(source)
                    : GetFirstPersonFallbackMaterial(kvp.Key, sprite: false);
                var tileModel = new GeometryModel3D(kvp.Value, material)
                {
                    BackMaterial = material
                };
                firstPerson3DTileArtGroup.Children.Add(tileModel);
            }

            firstPerson3DSpriteGroup.Children.Clear();
            foreach (var kvp in spriteArtMeshes)
            {
                if (kvp.Value.Positions.Count == 0) continue;
                Material material = spriteArtSources.TryGetValue(kvp.Key, out ImageSource? source) && source != null
                    ? GetFirstPersonTextureMaterial(source)
                    : GetFirstPersonFallbackMaterial(kvp.Key, sprite: true);
                var spriteModel = new GeometryModel3D(kvp.Value, material)
                {
                    BackMaterial = material
                };
                firstPerson3DSpriteGroup.Children.Add(spriteModel);
            }
            firstPerson3DHasMesh = true;
            firstPerson3DLastRebuildMs = Environment.TickCount64;
            firstPerson3DLastMinX = minX;
            firstPerson3DLastMaxX = maxX;
            firstPerson3DLastMinY = minY;
            firstPerson3DLastMaxY = maxY;
            firstPerson3DLastBackgroundTint = backgroundTint;
            firstPerson3DLastTileTint = tileTint;
            firstPerson3DLastGroundTint = groundTint;
            firstPerson3DLastForceSolidBlack = backgroundForceSolidBlack;
        }

        private void InvalidateFirstPerson3DPalette()
        {
            firstPerson3DHasMesh = false;
            firstPerson3DLastRebuildMs = -1;
            firstPerson3DTextureMaterials.Clear();
            firstPerson3DAlphaMasks.Clear();
        }

        private void UpdateFirstPersonScenePalette()
        {
            try
            {
                Color baseColor = backgroundForceSolidBlack
                    ? Color.FromRgb(3, 5, 10)
                    : backgroundTint;
                if (baseColor.A == 0 ||
                    (baseColor.R + baseColor.G + baseColor.B) < 18)
                    baseColor = Color.FromRgb(9, 20, 38);

                Color top = BlendFirstPersonColor(baseColor, Color.FromRgb(35, 68, 118), 0.34);
                Color middle = BlendFirstPersonColor(baseColor, Color.FromRgb(8, 14, 28), 0.36);
                Color bottom = BlendFirstPersonColor(baseColor, Colors.Black, 0.78);
                if (FirstPersonBackdrop != null)
                {
                    FirstPersonBackdrop.Fill = new LinearGradientBrush(
                        new GradientStopCollection
                        {
                            new GradientStop(top, 0),
                            new GradientStop(middle, 0.58),
                            new GradientStop(bottom, 1)
                        },
                        new Point(0, 0),
                        new Point(0, 1));
                }

                Color floor = groundTint.A == 0
                    ? Color.FromRgb(25, 29, 39)
                    : BlendFirstPersonColor(groundTint, Color.FromRgb(15, 18, 26), 0.56);
                firstPerson3DFloorMaterial = CreateMaterial(floor);
                if (firstPerson3DFloorModel != null)
                {
                    firstPerson3DFloorModel.Material = firstPerson3DFloorMaterial;
                    firstPerson3DFloorModel.BackMaterial = firstPerson3DFloorMaterial;
                }
            }
            catch { }
        }

        private static Color BlendFirstPersonColor(Color a, Color b, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromRgb(
                (byte)Math.Round(a.R + (b.R - a.R) * amount),
                (byte)Math.Round(a.G + (b.G - a.G) * amount),
                (byte)Math.Round(a.B + (b.B - a.B) * amount));
        }

        private int ResolveFirstPersonTileIndex(int tileId)
        {
            try
            {
                int animated = MapAnimatedTileIndex(tileId);
                int resolved = ResolveSimulatorTileIndex(animated);
                if (resolved >= 0 && resolved < (tileImages?.Length ?? 0))
                    return resolved;
            }
            catch { }
            return tileId;
        }

        private ImageSource? GetFirstPersonTileImage(int visualId, int originalId)
        {
            try
            {
                if (tileTonedImages != null && visualId >= 0 && visualId < tileTonedImages.Length &&
                    tileTonedImages[visualId] != null)
                    return tileTonedImages[visualId];
                if (tileImages != null && visualId >= 0 && visualId < tileImages.Length &&
                    tileImages[visualId] != null)
                    return tileImages[visualId];
                if (tileTonedImages != null && originalId >= 0 && originalId < tileTonedImages.Length &&
                    tileTonedImages[originalId] != null)
                    return tileTonedImages[originalId];
                if (tileImages != null && originalId >= 0 && originalId < tileImages.Length)
                    return tileImages[originalId];
            }
            catch { }
            return null;
        }

        private ImageSource? GetFirstPersonSpriteImage(int spriteId)
        {
            try
            {
                int visualId = spriteId == 0x7B ? 0x05 : spriteId == 0x7C ? 0x27 : spriteId;
                // Random portals borrow one of the real 24x48 portal images in
                // the 2D renderer. Use that source for their physical footprint
                // even though the procedural material supplies the rainbow color.
                if ((spriteId == 0x64 || spriteId == 0x7E) &&
                    previewSpriteMap != null &&
                    previewSpriteMap.TryGetValue(0x00, out ImageSource? portalPreview) &&
                    portalPreview != null)
                    return portalPreview;
                if (animationFrames != null &&
                    animationFrames.TryGetValue(visualId, out ImageSource?[]? frames) &&
                    frames != null && frames.Length > 0)
                {
                    int frame = Math.Abs(GetEditorAnimationFrameValue() / 8) % frames.Length;
                    if (frames[frame] != null) return frames[frame];
                }
                if (previewSpriteMap != null &&
                    previewSpriteMap.TryGetValue(visualId, out ImageSource? preview) &&
                    preview != null)
                    return preview;
                if (spriteImages != null && visualId >= 0 && visualId < spriteImages.Length)
                    return spriteImages[visualId];
            }
            catch { }
            return null;
        }

        private void AddFirstPersonInteractiveModel(
            int sid,
            bool isPortal,
            bool isCoin,
            bool isPad,
            double left,
            double right,
            double bottom,
            double top,
            int spriteIndex)
        {
            if (firstPerson3DInteractiveGroup == null) return;

            double cx = (left + right) * 0.5;
            double cy = (top + bottom) * 0.5;
            double phase = (spriteIndex * 47) % 360;
            int materialKey = isCoin
                ? (SharedPhysics.IsMiniCoinSprite(sid) ? 109 : 108)
                : isPad
                    ? GetPadMarkerMaterialKey(sid)
                    : GetSpriteMarkerMaterialKey(sid, isPortal);
            Material bodyMaterial = GetSpriteMarkerMaterial(materialKey);
            Material accentMaterial = GetSpriteMarkerMaterial(206);
            var model = new Model3DGroup();
            var bodyMesh = new MeshGeometry3D();
            var accentMesh = new MeshGeometry3D();
            int kind;
            Vector3D spinAxis;
            double visualWidth = Math.Max(0.5, right - left);
            double visualHeight = Math.Max(0.5, top - bottom);
            ScaleTransform3D sourceScale;

            if (isPortal)
            {
                kind = 3;
                spinAxis = new Vector3D(0, 1, 0);
                // Portal source art is normally 24x48 (1.5x3 tiles). Build a
                // normalized 1x2 portal, then scale to that actual footprint.
                AddTorus(bodyMesh, 0, 0, 0, 0.42, 0.080, 28, 10, 2.0);
                AddTorus(accentMesh, 0, 0, 0, 0.29, 0.032, 24, 8, 2.05);
                AddTorus(accentMesh, 0, 0, 0, 0.35, 0.020, 24, 7, 2.0, Math.PI / 24.0);
                for (int i = 0; i < 8; i++)
                {
                    double a = Math.PI * 2.0 * i / 8.0;
                    AddEllipsoid(
                        accentMesh,
                        Math.Cos(a) * 0.42,
                        Math.Sin(a) * 0.42 * 2.0,
                        0,
                        0.052, 0.052, 0.052,
                        8, 5);
                }
                sourceScale = new ScaleTransform3D(
                    visualWidth / 1.0,
                    visualHeight / 2.0,
                    Math.Max(1.0, visualWidth));
            }
            else if (isCoin)
            {
                kind = 4;
                spinAxis = new Vector3D(0, 1, 0);
                double scale = SharedPhysics.IsMiniCoinSprite(sid) ? 0.72 : 1.0;
                AddTorus(bodyMesh, 0, 0, 0, 0.245 * scale, 0.060 * scale, 20, 8);
                AddCylinderZ(bodyMesh, 0, 0, -0.055 * scale, 0.055 * scale, 0.13 * scale, 16);
                AddTorus(accentMesh, 0, 0, 0, 0.13 * scale, 0.018 * scale, 16, 6);
                sourceScale = new ScaleTransform3D(
                    visualWidth / 0.62,
                    visualHeight / 0.62,
                    Math.Max(visualWidth, visualHeight) / 0.62);
            }
            else if (isPad)
            {
                kind = 2;
                spinAxis = new Vector3D(0, 0, 1);
                AddBox(bodyMesh, -0.43, -0.18, -0.25, 0.43, -0.08, 0.25);
                AddTrianglePrism2D(
                    bodyMesh,
                    new Point(-0.38, -0.08),
                    new Point(0.38, -0.08),
                    new Point(0.27, 0.03),
                    -0.24, 0.24);
                for (int i = 0; i < 3; i++)
                {
                    double y = 0.02 + (i * 0.075);
                    AddTorus(accentMesh, 0, y, 0, 0.25 - i * 0.025, 0.025, 14, 6, 0.28);
                }
                AddBox(accentMesh, -0.30, 0.20, -0.20, 0.30, 0.25, 0.20);
                sourceScale = new ScaleTransform3D(
                    visualWidth,
                    Math.Max(0.8, visualHeight),
                    Math.Max(0.8, visualWidth));
            }
            else
            {
                kind = 1;
                spinAxis = new Vector3D(0.45, 0.75, 1.0);
                double scale = 1.0;
                if (SharedPhysics.IsYellowOrbBigger(sid)) scale = 1.2;
                else if (SharedPhysics.IsYellowOrbSmaller(sid)) scale = 0.85;
                // A solid spherical core plus three perpendicular energy rings
                // remains visibly three-dimensional even near the front view.
                AddEllipsoid(bodyMesh, 0, 0, 0, 0.24 * scale, 0.24 * scale, 0.24 * scale, 16, 10);
                AddTorus(accentMesh, 0, 0, 0, 0.31 * scale, 0.035 * scale, 20, 7);
                AddTorus(accentMesh, 0, 0, 0, 0.31 * scale, 0.035 * scale, 20, 7, plane: 1);
                AddTorus(accentMesh, 0, 0, 0, 0.31 * scale, 0.035 * scale, 20, 7, plane: 2);
                for (int i = 0; i < 4; i++)
                {
                    double a = Math.PI * 2.0 * i / 4.0;
                    AddEllipsoid(
                        accentMesh,
                        Math.Cos(a) * 0.31 * scale,
                        Math.Sin(a) * 0.31 * scale,
                        0,
                        0.035 * scale, 0.035 * scale, 0.035 * scale,
                        7, 4);
                }
                sourceScale = new ScaleTransform3D(
                    visualWidth / 0.90,
                    visualHeight / 0.90,
                    Math.Max(visualWidth, visualHeight) / 0.90);
            }

            AddGeometryModel(model, bodyMesh, bodyMaterial);
            AddGeometryModel(model, accentMesh, accentMaterial);

            var pulse = new ScaleTransform3D(1, 1, 1);
            var spin = new AxisAngleRotation3D(spinAxis, isPortal || isPad ? 0 : phase);
            var transforms = new Transform3DGroup();
            if (isPad && IsDownFacingPad(sid))
                transforms.Children.Add(new ScaleTransform3D(1, -1, 1));
            transforms.Children.Add(sourceScale);
            transforms.Children.Add(pulse);
            transforms.Children.Add(new RotateTransform3D(spin));
            transforms.Children.Add(new TranslateTransform3D(cx, cy, 0.70));
            model.Transform = transforms;

            firstPerson3DInteractiveGroup.Children.Add(model);
            firstPerson3DInteractiveInstances.Add(
                new FirstPerson3DInteractiveInstance(spin, pulse, kind, phase));
        }

        private void UpdateFirstPersonInteractiveAnimations()
        {
            if (firstPerson3DInteractiveInstances.Count == 0) return;
            double time = Environment.TickCount64 / 1000.0;
            foreach (FirstPerson3DInteractiveInstance item in firstPerson3DInteractiveInstances)
            {
                double radians = (time * 4.0) + (item.Phase * Math.PI / 180.0);
                switch (item.Kind)
                {
                    case 1: // Orb
                        item.Spin.Angle = (time * 72.0 + item.Phase) % 360.0;
                        double orbPulse = 1.0 + Math.Sin(radians) * 0.075;
                        item.Pulse.ScaleX = orbPulse;
                        item.Pulse.ScaleY = orbPulse;
                        item.Pulse.ScaleZ = orbPulse;
                        break;
                    case 2: // Pad
                        item.Spin.Angle = 0;
                        item.Pulse.ScaleX = 1.0;
                        item.Pulse.ScaleY = 1.0 + (0.5 + 0.5 * Math.Sin(radians * 1.35)) * 0.18;
                        item.Pulse.ScaleZ = 1.0;
                        break;
                    case 3: // Portal
                        item.Spin.Angle = 0;
                        item.Pulse.ScaleX = 1.0;
                        item.Pulse.ScaleY = 1.0;
                        item.Pulse.ScaleZ = 1.0;
                        break;
                    case 4: // Coin
                        item.Spin.Angle = (time * 105.0 + item.Phase) % 360.0;
                        break;
                }
            }
        }

        private static bool IsDownFacingPad(int sid)
        {
            return sid == 0x0A || sid == 0x25 || sid == 0x52 ||
                sid == 0x0D || sid == 0xFD || sid == 0x57;
        }

        private static void GetFirstPersonPortalFootprint(
            int sid,
            out double widthTiles,
            out double heightTiles)
        {
            // Horizontal gravity portals and horizontal teleport pairs use the
            // same physical portal size rotated 90 degrees.
            bool horizontal = (sid >= 0x10 && sid <= 0x13) ||
                (sid >= 0x66 && sid <= 0x69);
            widthTiles = horizontal ? 3.0 : 1.5;
            heightTiles = horizontal ? 1.5 : 3.0;
        }

        private bool IsFirstPerson3DInvisibleSprite(int sid)
        {
            // Keep 3D visibility identical to the simulator's normal renderer.
            // These records still execute; only their scene geometry is omitted.
            if (SharedPhysics.IsColorTriggerSprite(sid) || IsHiddenTriggerSprite(sid))
                return true;
            if (sid == 0x8F || sid == 0xCF) return true;
            if (sid >= 0x70 && sid <= 0x74) return true;
            if (sid == 0xEE || sid == 0xEF || sid == 0xFB || sid == 0xFC)
                return true;
            return false;
        }

        private Material GetFirstPersonTextureMaterial(ImageSource source)
        {
            ImageSource renderable = GetCachedRenderableImage(source);
            if (firstPerson3DTextureMaterials.TryGetValue(renderable, out Material? cached))
                return cached;

            var brush = new ImageBrush(renderable)
            {
                Stretch = Stretch.Fill,
                TileMode = TileMode.None,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
            var material = new DiffuseMaterial(brush);
            firstPerson3DTextureMaterials[renderable] = material;
            return material;
        }

        private MaterialGroup GetFirstPersonFallbackMaterial(int id, bool sprite)
        {
            int key = (sprite ? 0x10000 : 0) | (id & 0xFFFF);
            if (firstPerson3DFallbackMaterials.TryGetValue(key, out MaterialGroup? material))
                return material;

            unchecked
            {
                uint hash = (uint)(id * 2654435761);
                byte r = (byte)(72 + ((hash >> 16) & 0x7F));
                byte g = (byte)(72 + ((hash >> 8) & 0x7F));
                byte b = (byte)(72 + (hash & 0x7F));
                if (sprite)
                {
                    r = (byte)Math.Min(255, r + 42);
                    g = (byte)Math.Min(255, g + 42);
                    b = (byte)Math.Min(255, b + 42);
                }
                material = CreateLitMaterial(Color.FromRgb(r, g, b), sprite ? 0.18 : 0.04);
            }
            firstPerson3DFallbackMaterials[key] = material;
            return material;
        }

        private bool[]? GetFirstPersonAlphaMask(ImageSource? source)
        {
            if (source == null) return null;
            ImageSource renderable = GetCachedRenderableImage(source);
            if (firstPerson3DAlphaMasks.TryGetValue(renderable, out bool[]? cached))
                return cached;
            if (renderable is not BitmapSource bitmap || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
                return null;

            try
            {
                BitmapSource bgra = bitmap.Format == PixelFormats.Bgra32 ||
                    bitmap.Format == PixelFormats.Pbgra32
                        ? bitmap
                        : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                int width = bgra.PixelWidth;
                int height = bgra.PixelHeight;
                int stride = width * 4;
                var pixels = new byte[stride * height];
                bgra.CopyPixels(pixels, stride, 0);

                int grid = FirstPersonArtworkMaskGrid;
                var mask = new bool[grid * grid];
                for (int gy = 0; gy < grid; gy++)
                {
                    int py0 = gy * height / grid;
                    int py1 = Math.Max(py0 + 1, (gy + 1) * height / grid);
                    py1 = Math.Min(height, py1);
                    for (int gx = 0; gx < grid; gx++)
                    {
                        int px0 = gx * width / grid;
                        int px1 = Math.Max(px0 + 1, (gx + 1) * width / grid);
                        px1 = Math.Min(width, px1);
                        bool opaque = false;
                        for (int py = py0; py < py1 && !opaque; py++)
                        {
                            int row = py * stride;
                            for (int px = px0; px < px1; px++)
                            {
                                if (pixels[row + px * 4 + 3] >= 24)
                                {
                                    opaque = true;
                                    break;
                                }
                            }
                        }
                        mask[gy * grid + gx] = opaque;
                    }
                }

                firstPerson3DAlphaMasks[renderable] = mask;
                return mask;
            }
            catch
            {
                return null;
            }
        }

        private static void AddTexturedArtworkGeometry(
            MeshGeometry3D mesh,
            bool[]? mask,
            double xMin,
            double xMax,
            double yMin,
            double yMax,
            double zNear,
            double zFar)
        {
            if (mask == null || mask.Length != FirstPersonArtworkMaskGrid * FirstPersonArtworkMaskGrid)
            {
                AddTexturedBox(mesh, xMin, yMin, zNear, xMax, yMax, zFar);
                return;
            }

            int grid = FirstPersonArtworkMaskGrid;
            int occupied = 0;
            for (int i = 0; i < mask.Length; i++)
                if (mask[i]) occupied++;
            if (occupied == 0) return;
            if (occupied == mask.Length)
            {
                AddTexturedBox(mesh, xMin, yMin, zNear, xMax, yMax, zFar);
                return;
            }

            double stepX = (xMax - xMin) / grid;
            double stepY = (yMax - yMin) / grid;

            // Merge contiguous opaque pixels on the front and back faces. The
            // source UVs remain exact, while the mesh count stays far below a
            // per-pixel voxel model.
            for (int cy = 0; cy < grid; cy++)
            {
                int cx = 0;
                while (cx < grid)
                {
                    while (cx < grid && !mask[cy * grid + cx]) cx++;
                    if (cx >= grid) break;
                    int runStart = cx;
                    while (cx < grid && mask[cy * grid + cx]) cx++;
                    int runEnd = cx;

                    double x0 = xMin + runStart * stepX;
                    double x1 = xMin + runEnd * stepX;
                    double yTop = yMax - cy * stepY;
                    double yBottom = yTop - stepY;
                    double u0 = runStart / (double)grid;
                    double u1 = runEnd / (double)grid;
                    double v0 = cy / (double)grid;
                    double v1 = (cy + 1) / (double)grid;
                    AddTexturedQuad(mesh,
                        new Point3D(x0, yBottom, zFar), new Point3D(x1, yBottom, zFar),
                        new Point3D(x1, yTop, zFar), new Point3D(x0, yTop, zFar),
                        new Point(u0, v1), new Point(u1, v1), new Point(u1, v0), new Point(u0, v0));
                    AddTexturedQuad(mesh,
                        new Point3D(x1, yBottom, zNear), new Point3D(x0, yBottom, zNear),
                        new Point3D(x0, yTop, zNear), new Point3D(x1, yTop, zNear),
                        new Point(u0, v1), new Point(u1, v1), new Point(u1, v0), new Point(u0, v0));
                }
            }

            // Only emit depth faces on the artwork's true alpha boundary.
            for (int cy = 0; cy < grid; cy++)
            {
                for (int cx = 0; cx < grid; cx++)
                {
                    if (!mask[cy * grid + cx]) continue;
                    double x0 = xMin + cx * stepX;
                    double x1 = x0 + stepX;
                    double yTop = yMax - cy * stepY;
                    double yBottom = yTop - stepY;
                    double u0 = cx / (double)grid;
                    double u1 = (cx + 1) / (double)grid;
                    double v0 = cy / (double)grid;
                    double v1 = (cy + 1) / (double)grid;

                    bool left = cx == 0 || !mask[cy * grid + cx - 1];
                    bool right = cx == grid - 1 || !mask[cy * grid + cx + 1];
                    bool top = cy == 0 || !mask[(cy - 1) * grid + cx];
                    bool bottom = cy == grid - 1 || !mask[(cy + 1) * grid + cx];
                    if (left)
                        AddTexturedQuad(mesh,
                            new Point3D(x0, yBottom, zNear), new Point3D(x0, yBottom, zFar),
                            new Point3D(x0, yTop, zFar), new Point3D(x0, yTop, zNear),
                            new Point(u0, v1), new Point(u1, v1), new Point(u1, v0), new Point(u0, v0));
                    if (right)
                        AddTexturedQuad(mesh,
                            new Point3D(x1, yBottom, zFar), new Point3D(x1, yBottom, zNear),
                            new Point3D(x1, yTop, zNear), new Point3D(x1, yTop, zFar),
                            new Point(u0, v1), new Point(u1, v1), new Point(u1, v0), new Point(u0, v0));
                    if (top)
                        AddTexturedQuad(mesh,
                            new Point3D(x0, yTop, zFar), new Point3D(x1, yTop, zFar),
                            new Point3D(x1, yTop, zNear), new Point3D(x0, yTop, zNear),
                            new Point(u0, v1), new Point(u1, v1), new Point(u1, v0), new Point(u0, v0));
                    if (bottom)
                        AddTexturedQuad(mesh,
                            new Point3D(x0, yBottom, zNear), new Point3D(x1, yBottom, zNear),
                            new Point3D(x1, yBottom, zFar), new Point3D(x0, yBottom, zFar),
                            new Point(u0, v1), new Point(u1, v1), new Point(u1, v0), new Point(u0, v0));
                }
            }
        }

        private static void AddCollisionTileGeometry(
            MetatileCollision col,
            double x0,
            double x1,
            double yBottom,
            double yTop,
            MeshGeometry3D wallMesh,
            MeshGeometry3D slabMesh,
            MeshGeometry3D hazardMesh)
        {
            const double zNear = -0.55;
            const double zFar = 0.55;

            if (IsPureDeathHazardCollision(col))
            {
                AddPureDeathHazardGeometry(col, x0, x1, yBottom, yTop, hazardMesh, zNear, zFar);
                return;
            }

            if (IsSpikeStyleCollision(col))
            {
                AddSpikeGeometry(col, x0, x1, yBottom, yTop, slabMesh, hazardMesh, zNear, zFar);
                return;
            }

            int solidCells = 0;
            int deathCells = 0;
            int cells = FirstPersonCollisionVoxelGrid * FirstPersonCollisionVoxelGrid;

            bool[] solidMask = new bool[cells];
            bool[] deathMask = new bool[cells];

            for (int cy = 0; cy < FirstPersonCollisionVoxelGrid; cy++)
            {
                for (int cx = 0; cx < FirstPersonCollisionVoxelGrid; cx++)
                {
                    int localX = cx * (16 / FirstPersonCollisionVoxelGrid) + (8 / FirstPersonCollisionVoxelGrid);
                    int localY = cy * (16 / FirstPersonCollisionVoxelGrid) + (8 / FirstPersonCollisionVoxelGrid);
                    bool solid = TileOccupiesPixel(col, localX, localY);
                    bool death = MetatileCollisionTable.TileKillsAtPixel(col, localX, localY);

                    int i = cy * FirstPersonCollisionVoxelGrid + cx;
                    solidMask[i] = solid;
                    deathMask[i] = death;
                    if (solid) solidCells++;
                    if (death) deathCells++;
                }
            }

            if (solidCells == cells && deathCells == 0)
            {
                AddBox(wallMesh, x0, yBottom, zNear, x1, yTop, zFar);
                return;
            }

            double step = 1.0 / FirstPersonCollisionVoxelGrid;

            // Emit only exposed surfaces so adjacent voxels do not create coplanar
            // internal faces that flicker due to z-fighting.
            EmitVoxelSurface(slabMesh, solidMask, x0, yTop, zNear, zFar, step, 0.0, FirstPersonCollisionVoxelGrid);
            EmitVoxelSurface(hazardMesh, deathMask, x0, yTop, zNear + 0.18, zFar - 0.18, step, step * 0.08, FirstPersonCollisionVoxelGrid);

            // If this tile is shape-empty in the sampled mask but still classifies as
            // death (rare mixed shapes), keep a tiny fallback marker.
            if (solidCells == 0 && deathCells > 0)
            {
                AddBox(hazardMesh, x0 + 0.36, yBottom + 0.36, -0.08, x1 - 0.36, yTop - 0.36, 0.08);
            }
        }

        private static void EmitVoxelSurface(
            MeshGeometry3D mesh,
            bool[] mask,
            double tileX0,
            double tileYTop,
            double zNear,
            double zFar,
            double step,
            double inset,
            int grid)
        {
            for (int cy = 0; cy < grid; cy++)
            {
                for (int cx = 0; cx < grid; cx++)
                {
                    int i = cy * grid + cx;
                    if (!mask[i]) continue;

                    double vx0 = tileX0 + (cx * step) + inset;
                    double vx1 = tileX0 + ((cx + 1) * step) - inset;
                    double vyTop = tileYTop - (cy * step) - inset;
                    double vyBottom = tileYTop - ((cy + 1) * step) + inset;
                    if (vx1 <= vx0 || vyTop <= vyBottom) continue;

                    bool leftExposed = (cx == 0) || !mask[(cy * grid) + (cx - 1)];
                    bool rightExposed = (cx == grid - 1) || !mask[(cy * grid) + (cx + 1)];
                    bool topExposed = (cy == 0) || !mask[((cy - 1) * grid) + cx];
                    bool bottomExposed = (cy == grid - 1) || !mask[((cy + 1) * grid) + cx];

                    // Always add front/back so visible silhouette remains solid.
                    AddQuad(mesh, new Point3D(vx0, vyBottom, zNear), new Point3D(vx1, vyBottom, zNear), new Point3D(vx1, vyTop, zNear), new Point3D(vx0, vyTop, zNear));
                    AddQuad(mesh, new Point3D(vx1, vyBottom, zFar), new Point3D(vx0, vyBottom, zFar), new Point3D(vx0, vyTop, zFar), new Point3D(vx1, vyTop, zFar));

                    if (leftExposed)
                    {
                        AddQuad(mesh, new Point3D(vx0, vyBottom, zFar), new Point3D(vx0, vyBottom, zNear), new Point3D(vx0, vyTop, zNear), new Point3D(vx0, vyTop, zFar));
                    }
                    if (rightExposed)
                    {
                        AddQuad(mesh, new Point3D(vx1, vyBottom, zNear), new Point3D(vx1, vyBottom, zFar), new Point3D(vx1, vyTop, zFar), new Point3D(vx1, vyTop, zNear));
                    }
                    if (topExposed)
                    {
                        AddQuad(mesh, new Point3D(vx0, vyTop, zNear), new Point3D(vx1, vyTop, zNear), new Point3D(vx1, vyTop, zFar), new Point3D(vx0, vyTop, zFar));
                    }
                    if (bottomExposed)
                    {
                        AddQuad(mesh, new Point3D(vx0, vyBottom, zFar), new Point3D(vx1, vyBottom, zFar), new Point3D(vx1, vyBottom, zNear), new Point3D(vx0, vyBottom, zNear));
                    }
                }
            }
        }

        private static bool IsSpikeStyleCollision(MetatileCollision col)
        {
            switch (col)
            {
                case MetatileCollision.COL_TOP_CENTER_SPIKE:
                case MetatileCollision.COL_BOTTOM_CENTER_SPIKE:
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
                case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
                case MetatileCollision.COL_BOTTOM_SPIKES:
                case MetatileCollision.COL_UP_LEFT_SPIKE:
                case MetatileCollision.COL_UP_RIGHT_SPIKE:
                case MetatileCollision.COL_UP_BOTH_SPIKES:
                case MetatileCollision.COL_DOWN_LEFT_SPIKE:
                case MetatileCollision.COL_DOWN_RIGHT_SPIKE:
                case MetatileCollision.COL_DOWN_BOTH_SPIKES:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsPortalSpriteSid(int sid)
        {
            if (SharedPhysics.IsSpeedPortal(sid) || SharedPhysics.IsGameModePortal(sid) || SharedPhysics.IsGravityPortal(sid) || SharedPhysics.IsMiniGrowthPortal(sid))
            {
                return true;
            }

            if (SharedPhysics.IsTeleportPortalEntrance(sid) ||
                SharedPhysics.IsTeleportPortalExit(sid) ||
                sid == 0x64 || sid == 0x7E)
            {
                return true;
            }

            // Portal-like special classes used by simulator logic.
            if (sid == 0x22 || sid == 0x23 || sid == 0x8E || sid == 0x9E)
            {
                return true;
            }

            // Gravity modifier portals (0x5F-0x63).
            if (sid >= 0x5F && sid <= 0x63)
            {
                return true;
            }

            return false;
        }

        private static int GetSpriteMarkerMaterialKey(int sid, bool isPortal)
        {
            if (!isPortal)
            {
                if (sid == 0x79) return 117;
                if (SharedPhysics.IsDashOrb(sid)) return 118;
                if (SharedPhysics.IsYellowOrb(sid) || SharedPhysics.IsYellowOrbBigger(sid) || SharedPhysics.IsYellowOrbSmaller(sid)) return 100;
                if (SharedPhysics.IsPinkOrb(sid)) return 101;
                if (SharedPhysics.IsRedOrb(sid)) return 102;
                if (SharedPhysics.IsBlueOrb(sid)) return 103;
                if (SharedPhysics.IsGreenOrb(sid)) return 104;
                if (SharedPhysics.IsBlackOrb(sid)) return 105;
                if (SharedPhysics.IsWhiteOrb(sid)) return 106;
                if (SharedPhysics.IsSpiderOrb(sid)) return 107;
                return 100;
            }

            if (SharedPhysics.IsGravityPortal(sid))
            {
                return SharedPhysics.IsReverseGravity(sid) ? 201 : 200;
            }
            if (sid == 0x64 || sid == 0x7E) return 209;
            if (SharedPhysics.IsTeleportPortalEntrance(sid) ||
                SharedPhysics.IsTeleportPortalExit(sid)) return 210;
            if (SharedPhysics.IsMiniGrowthPortal(sid))
            {
                return sid == 0x18 ? 202 : 203;
            }
            if (SharedPhysics.IsSpeedPortal(sid)) return 204;
            if (SharedPhysics.IsGameModePortal(sid))
            {
                int mode = SharedPhysics.SpriteIdToGameMode(sid);
                return mode >= 0 ? 220 + mode : 205;
            }
            if (sid == 0x22 || sid == 0x23) return 206;
            if (sid == 0x8E || sid == 0x9E) return 207;
            if (sid >= 0x5F && sid <= 0x63) return 208;
            return 205;
        }

        private static int GetPadMarkerMaterialKey(int sid)
        {
            if (SharedPhysics.IsYellowPad(sid)) return 111;
            if (SharedPhysics.IsPinkPad(sid)) return 112;
            if (SharedPhysics.IsRedPad(sid)) return 113;
            if (SharedPhysics.IsBluePad(sid)) return 114;
            if (SharedPhysics.IsGreenPad(sid)) return 115;
            if (SharedPhysics.IsSpiderPad(sid)) return 116;
            return 111;
        }

        private MaterialGroup GetSpriteMarkerMaterial(int key)
        {
            if (firstPerson3DMarkerMaterials.TryGetValue(key, out MaterialGroup? existing))
            {
                return existing;
            }

            Color diffuse;
            Color emissive;
            switch (key)
            {
                case 100: diffuse = Color.FromRgb(245, 222, 68); emissive = Color.FromArgb(170, 255, 244, 120); break; // yellow orb
                case 101: diffuse = Color.FromRgb(255, 120, 220); emissive = Color.FromArgb(170, 255, 180, 235); break; // pink orb
                case 102: diffuse = Color.FromRgb(255, 92, 78); emissive = Color.FromArgb(165, 255, 150, 130); break;  // red orb
                case 103: diffuse = Color.FromRgb(86, 172, 255); emissive = Color.FromArgb(170, 138, 214, 255); break; // blue orb
                case 104: diffuse = Color.FromRgb(90, 238, 112); emissive = Color.FromArgb(170, 140, 255, 168); break; // green orb
                case 105: diffuse = Color.FromRgb(30, 30, 36); emissive = Color.FromArgb(120, 110, 110, 140); break;   // black orb
                case 106: diffuse = Color.FromRgb(245, 245, 245); emissive = Color.FromArgb(170, 255, 255, 255); break; // white orb
                case 107: diffuse = Color.FromRgb(170, 118, 255); emissive = Color.FromArgb(170, 205, 175, 255); break; // spider orb
                case 108: diffuse = Color.FromRgb(255, 218, 62); emissive = Color.FromArgb(185, 255, 235, 120); break; // coin
                case 109: diffuse = Color.FromRgb(182, 242, 255); emissive = Color.FromArgb(185, 210, 250, 255); break; // mini coin
                case 111: diffuse = Color.FromRgb(255, 218, 62); emissive = Color.FromArgb(165, 255, 235, 120); break; // yellow pad
                case 112: diffuse = Color.FromRgb(255, 118, 220); emissive = Color.FromArgb(165, 255, 176, 235); break; // pink pad
                case 113: diffuse = Color.FromRgb(255, 82, 70); emissive = Color.FromArgb(165, 255, 145, 125); break; // red pad
                case 114: diffuse = Color.FromRgb(76, 166, 255); emissive = Color.FromArgb(165, 135, 215, 255); break; // blue pad
                case 115: diffuse = Color.FromRgb(82, 238, 108); emissive = Color.FromArgb(165, 135, 255, 160); break; // green pad
                case 116: diffuse = Color.FromRgb(170, 118, 255); emissive = Color.FromArgb(165, 210, 170, 255); break; // spider pad
                case 117: diffuse = Color.FromRgb(62, 66, 78); emissive = Color.FromArgb(185, 255, 72, 72); break; // skull/death orb
                case 118: diffuse = Color.FromRgb(255, 150, 70); emissive = Color.FromArgb(180, 255, 205, 120); break; // dash orb
                case 200: diffuse = Color.FromRgb(112, 228, 255); emissive = Color.FromArgb(170, 160, 245, 255); break; // gravity portal normal
                case 201: diffuse = Color.FromRgb(255, 118, 232); emissive = Color.FromArgb(170, 255, 155, 245); break; // gravity portal reverse
                case 202: diffuse = Color.FromRgb(114, 255, 148); emissive = Color.FromArgb(170, 165, 255, 190); break; // mini
                case 203: diffuse = Color.FromRgb(255, 176, 96); emissive = Color.FromArgb(170, 255, 214, 156); break;  // growth
                case 204: diffuse = Color.FromRgb(255, 215, 78); emissive = Color.FromArgb(170, 255, 235, 146); break;  // speed portal
                case 205: diffuse = Color.FromRgb(152, 168, 255); emissive = Color.FromArgb(170, 190, 210, 255); break; // gamemode portal
                case 206: diffuse = Color.FromRgb(248, 248, 248); emissive = Color.FromArgb(170, 255, 255, 255); break; // dual/single
                case 207: diffuse = Color.FromRgb(110, 255, 244); emissive = Color.FromArgb(170, 150, 255, 250); break; // wrap
                case 208: diffuse = Color.FromRgb(255, 137, 96); emissive = Color.FromArgb(170, 255, 180, 150); break;  // gravity modifier
                case 209: diffuse = Color.FromRgb(255, 112, 224); emissive = Color.FromArgb(180, 140, 235, 255); break; // random/rainbow
                case 210: diffuse = Color.FromRgb(92, 255, 224); emissive = Color.FromArgb(180, 155, 255, 242); break; // teleport
                case 220: diffuse = Color.FromRgb(105, 255, 132); emissive = Color.FromArgb(175, 155, 255, 180); break; // cube
                case 221: diffuse = Color.FromRgb(255, 128, 72); emissive = Color.FromArgb(175, 255, 182, 125); break; // ship
                case 222: diffuse = Color.FromRgb(255, 105, 210); emissive = Color.FromArgb(175, 255, 165, 230); break; // ball
                case 223: diffuse = Color.FromRgb(112, 210, 255); emissive = Color.FromArgb(175, 165, 230, 255); break; // ufo
                case 224: diffuse = Color.FromRgb(175, 132, 255); emissive = Color.FromArgb(175, 210, 180, 255); break; // robot
                case 225: diffuse = Color.FromRgb(255, 225, 92); emissive = Color.FromArgb(175, 255, 240, 150); break; // spider
                case 226: diffuse = Color.FromRgb(78, 236, 255); emissive = Color.FromArgb(175, 145, 250, 255); break; // wave
                case 227: diffuse = Color.FromRgb(255, 105, 105); emissive = Color.FromArgb(175, 255, 165, 150); break; // swing
                case 228: diffuse = Color.FromRgb(125, 255, 208); emissive = Color.FromArgb(175, 180, 255, 230); break; // ninja
                case 229: diffuse = Color.FromRgb(242, 154, 255); emissive = Color.FromArgb(175, 250, 195, 255); break; // pogo
                case 230: diffuse = Color.FromRgb(126, 224, 96); emissive = Color.FromArgb(175, 185, 255, 150); break; // snake
                case 231: diffuse = Color.FromRgb(238, 238, 244); emissive = Color.FromArgb(175, 255, 255, 255); break; // football/later
                default: diffuse = Color.FromRgb(190, 190, 220); emissive = Color.FromArgb(140, 210, 210, 245); break;
            }

            var d = new DiffuseMaterial(new SolidColorBrush(diffuse));
            // A strong emissive layer made rounded models look like flat icons.
            // Preserve a restrained glow while allowing scene lights and a
            // specular highlight to reveal the actual surface curvature.
            var restrainedGlow = Color.FromArgb(
                (byte)Math.Min(88, Math.Max(28, emissive.A / 2)),
                emissive.R, emissive.G, emissive.B);
            var e = new EmissiveMaterial(new SolidColorBrush(restrainedGlow));
            var specular = new SpecularMaterial(
                new SolidColorBrush(Color.FromArgb(185, 255, 255, 255)), 52);
            if (d.CanFreeze) d.Freeze();
            if (e.CanFreeze) e.Freeze();
            if (specular.CanFreeze) specular.Freeze();

            var group = new MaterialGroup();
            group.Children.Add(d);
            group.Children.Add(e);
            group.Children.Add(specular);
            firstPerson3DMarkerMaterials[key] = group;
            return group;
        }

        private static void AddRadialRing(
            MeshGeometry3D mesh,
            double cx,
            double cy,
            double cz,
            double outerR,
            double innerR,
            double halfDepth,
            int segments,
            double yScale = 1.0)
        {
            if (segments < 3) segments = 3;
            for (int s = 0; s < segments; s++)
            {
                double a0 = (Math.PI * 2.0 * s) / segments;
                double a1 = (Math.PI * 2.0 * (s + 1)) / segments;

                double c0 = Math.Cos(a0);
                double n0 = Math.Sin(a0);
                double c1 = Math.Cos(a1);
                double n1 = Math.Sin(a1);

                Point3D o0f = new Point3D(cx + (outerR * c0), cy + (outerR * n0 * yScale), cz - halfDepth);
                Point3D o1f = new Point3D(cx + (outerR * c1), cy + (outerR * n1 * yScale), cz - halfDepth);
                Point3D i0f = new Point3D(cx + (innerR * c0), cy + (innerR * n0 * yScale), cz - halfDepth);
                Point3D i1f = new Point3D(cx + (innerR * c1), cy + (innerR * n1 * yScale), cz - halfDepth);

                Point3D o0b = new Point3D(o0f.X, o0f.Y, cz + halfDepth);
                Point3D o1b = new Point3D(o1f.X, o1f.Y, cz + halfDepth);
                Point3D i0b = new Point3D(i0f.X, i0f.Y, cz + halfDepth);
                Point3D i1b = new Point3D(i1f.X, i1f.Y, cz + halfDepth);

                // Front and back annulus slices.
                AddQuad(mesh, o0f, o1f, i1f, i0f);
                AddQuad(mesh, o1b, o0b, i0b, i1b);

                // Outer and inner side walls.
                AddQuad(mesh, o0f, o0b, o1b, o1f);
                AddQuad(mesh, i1f, i1b, i0b, i0f);
            }
        }

        private static void AddSpikeGeometry(
            MetatileCollision col,
            double x0,
            double x1,
            double yBottom,
            double yTop,
            MeshGeometry3D slabMesh,
            MeshGeometry3D hazardMesh,
            double zNear,
            double zFar)
        {
            double xMid = (x0 + x1) * 0.5;
            double yMid = (yTop + yBottom) * 0.5;

            void AddTopSlab() => AddBox(slabMesh, x0, yMid, zNear, x1, yTop, zFar);
            void AddBottomSlab() => AddBox(slabMesh, x0, yBottom, zNear, x1, yMid, zFar);
            void AddBottomLeftQuarter() => AddBox(slabMesh, x0, yBottom, zNear, xMid, yMid, zFar);
            void AddBottomRightQuarter() => AddBox(slabMesh, xMid, yBottom, zNear, x1, yMid, zFar);

            void AddCenterSpikeDown() => AddSpikePrism(hazardMesh, x0 + 0.18, x1 - 0.18, yMid, xMid, yBottom + 0.02, zNear, zFar);
            void AddCenterSpikeUp() => AddSpikePrism(hazardMesh, x0 + 0.18, x1 - 0.18, yMid, xMid, yTop - 0.02, zNear, zFar);
            void AddLeftSpikeUp() => AddSpikePrism(hazardMesh, x0 + 0.04, x0 + 0.52, yMid, x0 + 0.28, yTop - 0.03, zNear, zFar);
            void AddRightSpikeUp() => AddSpikePrism(hazardMesh, x1 - 0.52, x1 - 0.04, yMid, x1 - 0.28, yTop - 0.03, zNear, zFar);
            void AddLeftSpikeDown() => AddSpikePrism(hazardMesh, x0 + 0.04, x0 + 0.52, yTop, x0 + 0.28, yMid + 0.02, zNear, zFar);
            void AddRightSpikeDown() => AddSpikePrism(hazardMesh, x1 - 0.52, x1 - 0.04, yTop, x1 - 0.28, yMid + 0.02, zNear, zFar);
            void AddLeftSpikeBottomUp() => AddSpikePrism(hazardMesh, x0 + 0.04, x0 + 0.52, yBottom, x0 + 0.28, yMid - 0.02, zNear, zFar);
            void AddRightSpikeBottomUp() => AddSpikePrism(hazardMesh, x1 - 0.52, x1 - 0.04, yBottom, x1 - 0.28, yMid - 0.02, zNear, zFar);

            switch (col)
            {
                case MetatileCollision.COL_TOP_CENTER_SPIKE:
                    AddTopSlab();
                    AddCenterSpikeDown();
                    break;
                case MetatileCollision.COL_BOTTOM_CENTER_SPIKE:
                    AddBottomSlab();
                    AddCenterSpikeUp();
                    break;
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                    AddBottomLeftQuarter();
                    AddLeftSpikeUp();
                    break;
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    AddBottomRightQuarter();
                    AddRightSpikeUp();
                    break;
                case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
                    AddBottomSlab();
                    AddLeftSpikeUp();
                    break;
                case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
                    AddBottomSlab();
                    AddRightSpikeUp();
                    break;
                case MetatileCollision.COL_BOTTOM_SPIKES:
                    AddBottomSlab();
                    AddLeftSpikeUp();
                    AddRightSpikeUp();
                    break;
                case MetatileCollision.COL_UP_LEFT_SPIKE:
                    AddLeftSpikeDown();
                    break;
                case MetatileCollision.COL_UP_RIGHT_SPIKE:
                    AddRightSpikeDown();
                    break;
                case MetatileCollision.COL_UP_BOTH_SPIKES:
                    AddLeftSpikeDown();
                    AddRightSpikeDown();
                    break;
                case MetatileCollision.COL_DOWN_LEFT_SPIKE:
                    AddLeftSpikeBottomUp();
                    break;
                case MetatileCollision.COL_DOWN_RIGHT_SPIKE:
                    AddRightSpikeBottomUp();
                    break;
                case MetatileCollision.COL_DOWN_BOTH_SPIKES:
                    AddLeftSpikeBottomUp();
                    AddRightSpikeBottomUp();
                    break;
            }
        }

        private static bool IsPureDeathHazardCollision(MetatileCollision col)
        {
            return col >= MetatileCollision.COL_DEATH &&
                col <= MetatileCollision.COL_DEATH_TOP_LEFT_BOTTOM;
        }

        private static void AddPureDeathHazardGeometry(
            MetatileCollision col,
            double x0,
            double x1,
            double yBottom,
            double yTop,
            MeshGeometry3D hazardMesh,
            double zNear,
            double zFar)
        {
            double xMid = (x0 + x1) * 0.5;
            double yMid = (yBottom + yTop) * 0.5;
            double zn = zNear + 0.08;
            double zf = zFar - 0.08;

            if (col == MetatileCollision.COL_DEATH)
            {
                // The NES center-death mask is a compact four-sided spike.
                AddEllipsoid(hazardMesh, xMid, yMid, 0, 0.30, 0.38, 0.30, 4, 2);
                return;
            }

            bool top = col == MetatileCollision.COL_DEATH_TOP ||
                col == MetatileCollision.COL_DEATH_TOP_RIGHT ||
                col == MetatileCollision.COL_DEATH_TOP_LEFT ||
                col == MetatileCollision.COL_DEATH_TOP_RIGHT_LEFT ||
                col == MetatileCollision.COL_DEATH_TOP_BOTTOM ||
                col == MetatileCollision.COL_DEATH_TOP_LEFT_BOTTOM;
            bool bottom = col == MetatileCollision.COL_DEATH_BOTTOM ||
                col == MetatileCollision.COL_DEATH_BOTTOM_RIGHT ||
                col == MetatileCollision.COL_DEATH_BOTTOM_LEFT ||
                col == MetatileCollision.COL_DEATH_TOP_BOTTOM ||
                col == MetatileCollision.COL_DEATH_TOP_LEFT_BOTTOM;
            bool left = col == MetatileCollision.COL_DEATH_LEFT ||
                col == MetatileCollision.COL_DEATH_TOP_LEFT ||
                col == MetatileCollision.COL_DEATH_BOTTOM_LEFT ||
                col == MetatileCollision.COL_DEATH_TOP_RIGHT_LEFT ||
                col == MetatileCollision.COL_DEATH_LEFT_RIGHT ||
                col == MetatileCollision.COL_DEATH_TOP_LEFT_BOTTOM;
            bool right = col == MetatileCollision.COL_DEATH_RIGHT ||
                col == MetatileCollision.COL_DEATH_TOP_RIGHT ||
                col == MetatileCollision.COL_DEATH_BOTTOM_RIGHT ||
                col == MetatileCollision.COL_DEATH_TOP_RIGHT_LEFT ||
                col == MetatileCollision.COL_DEATH_LEFT_RIGHT;

            if (top)
            {
                AddTrianglePrism2D(hazardMesh,
                    new Point(xMid - 0.22, yTop - 0.02),
                    new Point(xMid + 0.22, yTop - 0.02),
                    new Point(xMid, yTop - 0.58), zn, zf);
            }
            if (bottom)
            {
                AddTrianglePrism2D(hazardMesh,
                    new Point(xMid - 0.22, yBottom + 0.02),
                    new Point(xMid, yBottom + 0.58),
                    new Point(xMid + 0.22, yBottom + 0.02), zn, zf);
            }
            if (left)
            {
                AddTrianglePrism2D(hazardMesh,
                    new Point(x0 + 0.02, yMid - 0.22),
                    new Point(x0 + 0.58, yMid),
                    new Point(x0 + 0.02, yMid + 0.22), zn, zf);
            }
            if (right)
            {
                AddTrianglePrism2D(hazardMesh,
                    new Point(x1 - 0.02, yMid - 0.22),
                    new Point(x1 - 0.02, yMid + 0.22),
                    new Point(x1 - 0.58, yMid), zn, zf);
            }
        }

        private double GetFirstPersonPlayerAngle(int mode, int playerIndex)
        {
            try
            {
                int velX = playerIndex == 1 && player_vel_x_fixed.Length > 1
                    ? player_vel_x_fixed[1]
                    : playerVelX_fixed;
                int velY = playerIndex == 1 && player_vel_y_fixed.Length > 1
                    ? player_vel_y_fixed[1]
                    : playerVelY_fixed;
                bool mini = playerIndex == 1 && player_mini.Length > 1
                    ? player_mini[1]
                    : miniMode;
                bool reversed = playerIndex == 1 && player_gravity.Length > 1
                    ? player_gravity[1] != 0
                    : gravityReversed;

                if (mode == 1 || mode == 7)
                {
                    // Famidash does not draw ship/swing at the continuous
                    // atan2 trajectory. drawplayerone uses the eight-entry
                    // velocity frame table, with duplicated endpoint/level
                    // frames. Match those exact visual angles.
                    int frame = ComputeNesVelocitySpriteFrame(
                        velY, velX, mini, reversed, waveOrSnake: false);
                    return frame switch
                    {
                        0 or 1 => -45.0,
                        2 => -22.5,
                        5 => 22.5,
                        6 or 7 => 45.0,
                        _ => 0.0
                    };
                }

                if (mode == 6)
                {
                    int frame = ComputeNesVelocitySpriteFrame(
                        velY, velX, mini, reversed, waveOrSnake: true);
                    if (mini)
                        return frame >= 5 ? 45.0 : frame <= 2 ? -45.0 : 0.0;
                    return frame == 2 ? -22.5 : frame == 5 ? 22.5 :
                        frame >= 6 ? 45.0 : frame <= 1 ? -45.0 : 0.0;
                }

                if (mode == 10)
                {
                    int frame = ComputeNesVelocitySpriteFrame(
                        velY, velX, mini, reversed, waveOrSnake: true);
                    return frame <= 1 ? -45.0 : frame >= 6 ? 45.0 : 0.0;
                }

                if (mode == 11)
                {
                    int rotation = playerIndex == 1 && player_footballRotate.Length > 1
                        ? player_footballRotate[1]
                        : footballRotate_fixed;
                    return -(((rotation >> 8) & 0xFF) * 15.0);
                }

                if (mode == 0 || mode == 4 || mode == 8)
                {
                    int rotation = playerIndex == 1 && player_cubeRotate.Length > 1
                        ? player_cubeRotate[1]
                        : cubeRotate_fixed;
                    // WPF's 2D positive angle is clockwise because screen Y grows
                    // downward. Media3D's +Z rotation is counter-clockwise, so the
                    // renderer must invert the NES/editor rotation sign.
                    return -(((rotation >> 8) & 0xFF) * 15.0);
                }

                if (mode == 2)
                    return -((simTickCount * 11.25) % 360.0);
            }
            catch { }
            return 0.0;
        }

        private void RebuildFirstPersonPlayerModel(
            Model3DGroup group,
            int mode,
            bool mini,
            bool playerTwo)
        {
            group.Children.Clear();
            Material body = playerTwo
                ? firstPerson3DPlayer2Material ?? GetFirstPersonFallbackMaterial(2, sprite: true)
                : firstPerson3DPlayerMaterial ?? GetFirstPersonFallbackMaterial(1, sprite: true);
            Material accent = firstPerson3DPlayerAccentMaterial ??
                GetFirstPersonFallbackMaterial(0xFFFFFF, sprite: true);
            double s = mini ? 0.62 : 1.0;

            var bodyMesh = new MeshGeometry3D();
            var accentMesh = new MeshGeometry3D();

            switch (mode)
            {
                case 1: // Ship: fuselage, cockpit, swept wings and tail.
                    AddEllipsoid(bodyMesh, 0.0, 0.47 * s, 0.0, 0.49 * s, 0.22 * s, 0.22 * s, 14, 8);
                    AddEllipsoid(bodyMesh, 0.40 * s, 0.47 * s, 0.0,
                        0.22 * s, 0.15 * s, 0.15 * s, 12, 7);
                    AddSpikePrism(bodyMesh, -0.28 * s, 0.31 * s, 0.37 * s,
                        -0.05 * s, 0.02 * s, -0.12 * s, 0.12 * s);
                    AddSpikePrism(bodyMesh, -0.28 * s, 0.31 * s, 0.56 * s,
                        -0.05 * s, 0.91 * s, -0.12 * s, 0.12 * s);
                    AddBox(bodyMesh, -0.46 * s, 0.36 * s, -0.10 * s,
                        -0.29 * s, 0.58 * s, 0.10 * s);
                    AddEllipsoid(accentMesh, 0.12 * s, 0.57 * s, 0.20 * s,
                        0.16 * s, 0.12 * s, 0.09 * s, 10, 6);
                    AddEllipsoid(accentMesh, -0.47 * s, 0.47 * s, 0,
                        0.07 * s, 0.13 * s, 0.13 * s, 10, 6);
                    break;

                case 2: // Ball.
                    AddEllipsoid(bodyMesh, 0, 0.48 * s, 0, 0.43 * s, 0.43 * s, 0.43 * s, 18, 10);
                    AddRadialRing(accentMesh, 0, 0.48 * s, 0, 0.31 * s, 0.25 * s, 0.445 * s, 18);
                    break;

                case 3: // UFO.
                    AddEllipsoid(bodyMesh, 0, 0.38 * s, 0, 0.48 * s, 0.17 * s, 0.35 * s, 18, 8);
                    AddRadialRing(bodyMesh, 0, 0.38 * s, 0, 0.50 * s, 0.37 * s, 0.13 * s, 20, 0.48);
                    AddEllipsoid(accentMesh, 0, 0.56 * s, 0, 0.23 * s, 0.20 * s, 0.21 * s, 14, 8);
                    AddCylinderY(accentMesh, 0, 0.04 * s, 0, 0.0, 0.23 * s, 0.055 * s, 8);
                    break;

                case 4: // Robot.
                    AddBox(bodyMesh, -0.31 * s, 0.35 * s, -0.27 * s, 0.31 * s, 0.82 * s, 0.27 * s);
                    AddBox(bodyMesh, -0.24 * s, 0.10 * s, -0.20 * s, -0.04 * s, 0.36 * s, 0.20 * s);
                    AddBox(bodyMesh, 0.04 * s, 0.10 * s, -0.20 * s, 0.24 * s, 0.36 * s, 0.20 * s);
                    AddBox(accentMesh, -0.19 * s, 0.57 * s, 0.265 * s, -0.07 * s, 0.69 * s, 0.30 * s);
                    AddBox(accentMesh, 0.07 * s, 0.57 * s, 0.265 * s, 0.19 * s, 0.69 * s, 0.30 * s);
                    break;

                case 5: // Spider.
                    AddEllipsoid(bodyMesh, 0, 0.48 * s, 0, 0.34 * s, 0.25 * s, 0.31 * s, 16, 8);
                    for (int i = 0; i < 4; i++)
                    {
                        double y = (0.31 + i * 0.11) * s;
                        double reach = (0.48 - i * 0.035) * s;
                        AddBeamPrism2D(bodyMesh, new Point(-0.18 * s, y), new Point(-reach, y - 0.18 * s), 0.055 * s, -0.12 * s, 0.12 * s);
                        AddBeamPrism2D(bodyMesh, new Point(0.18 * s, y), new Point(reach, y - 0.18 * s), 0.055 * s, -0.12 * s, 0.12 * s);
                    }
                    AddEllipsoid(accentMesh, -0.12 * s, 0.54 * s, 0.285 * s, 0.055 * s, 0.055 * s, 0.035 * s, 8, 5);
                    AddEllipsoid(accentMesh, 0.12 * s, 0.54 * s, 0.285 * s, 0.055 * s, 0.055 * s, 0.035 * s, 8, 5);
                    break;

                case 6: // Wave.
                    AddSpikePrism(bodyMesh, -0.46 * s, 0.09 * s, 0.16 * s,
                        0.48 * s, 0.50 * s, -0.18 * s, 0.18 * s);
                    AddSpikePrism(bodyMesh, -0.46 * s, 0.09 * s, 0.84 * s,
                        0.48 * s, 0.50 * s, -0.18 * s, 0.18 * s);
                    AddBox(accentMesh, -0.30 * s, 0.43 * s, 0.18 * s,
                        0.12 * s, 0.57 * s, 0.22 * s);
                    break;

                case 7: // Swingcopter.
                    AddEllipsoid(bodyMesh, 0, 0.44 * s, 0, 0.27 * s, 0.25 * s, 0.25 * s, 14, 8);
                    AddCylinderY(bodyMesh, 0, 0.18 * s, 0, 0.32 * s, 0.70 * s, 0.075 * s, 10);
                    AddBox(bodyMesh, -0.47 * s, 0.70 * s, -0.055 * s, 0.47 * s, 0.78 * s, 0.055 * s);
                    AddBox(accentMesh, -0.08 * s, 0.36 * s, 0.245 * s, 0.08 * s, 0.52 * s, 0.29 * s);
                    break;

                case 8: // Ninja.
                    AddEllipsoid(bodyMesh, 0, 0.48 * s, 0, 0.23 * s, 0.23 * s, 0.20 * s, 12, 7);
                    AddTrianglePrism2D(bodyMesh,
                        new Point(-0.13 * s, 0.53 * s), new Point(0.13 * s, 0.53 * s),
                        new Point(0, 0.98 * s), -0.14 * s, 0.14 * s);
                    AddTrianglePrism2D(bodyMesh,
                        new Point(0.13 * s, 0.43 * s), new Point(-0.13 * s, 0.43 * s),
                        new Point(0, 0.00 * s), -0.14 * s, 0.14 * s);
                    AddTrianglePrism2D(bodyMesh,
                        new Point(-0.03 * s, 0.36 * s), new Point(-0.03 * s, 0.62 * s),
                        new Point(-0.49 * s, 0.49 * s), -0.14 * s, 0.14 * s);
                    AddTrianglePrism2D(bodyMesh,
                        new Point(0.03 * s, 0.62 * s), new Point(0.03 * s, 0.36 * s),
                        new Point(0.49 * s, 0.49 * s), -0.14 * s, 0.14 * s);
                    AddEllipsoid(accentMesh, 0, 0.48 * s, 0.205 * s, 0.08 * s, 0.08 * s, 0.035 * s, 8, 5);
                    break;

                case 9: // Pogo.
                    AddBox(bodyMesh, -0.30 * s, 0.47 * s, -0.26 * s, 0.30 * s, 0.90 * s, 0.26 * s);
                    AddCylinderY(bodyMesh, 0, 0.12 * s, 0, 0.13 * s, 0.48 * s, 0.065 * s, 10);
                    AddBox(bodyMesh, -0.38 * s, 0.05 * s, -0.12 * s, 0.38 * s, 0.13 * s, 0.12 * s);
                    for (int i = 0; i < 3; i++)
                        AddRadialRing(accentMesh, 0, (0.20 + i * 0.09) * s, 0,
                            0.14 * s, 0.095 * s, 0.075 * s, 10, 0.32);
                    break;

                case 10: // Snake.
                    for (int i = 0; i < 5; i++)
                    {
                        double x = (-0.34 + i * 0.17) * s;
                        double y = (0.36 + ((i & 1) == 0 ? 0.11 : 0.0)) * s;
                        double radius = (i == 4 ? 0.18 : 0.14) * s;
                        AddEllipsoid(bodyMesh, x, y, 0, radius, radius, radius, 10, 6);
                    }
                    AddEllipsoid(accentMesh, 0.38 * s, 0.52 * s, 0.15 * s,
                        0.045 * s, 0.045 * s, 0.03 * s, 8, 5);
                    break;

                case 11: // Football.
                    AddEllipsoid(bodyMesh, 0, 0.48 * s, 0, 0.47 * s, 0.31 * s, 0.31 * s, 18, 10);
                    AddBox(accentMesh, -0.18 * s, 0.455 * s, 0.305 * s, 0.18 * s, 0.505 * s, 0.34 * s);
                    for (int i = -2; i <= 2; i++)
                        AddBox(accentMesh, i * 0.065 * s - 0.018 * s, 0.41 * s, 0.31 * s,
                            i * 0.065 * s + 0.018 * s, 0.55 * s, 0.35 * s);
                    break;

                case 0: // Cube.
                default:
                    AddBox(bodyMesh, -0.43 * s, 0.05 * s, -0.43 * s, 0.43 * s, 0.91 * s, 0.43 * s);
                    AddBox(accentMesh, -0.27 * s, 0.21 * s, 0.425 * s, 0.27 * s, 0.75 * s, 0.47 * s);
                    AddBox(bodyMesh, -0.17 * s, 0.31 * s, 0.465 * s, 0.17 * s, 0.65 * s, 0.50 * s);
                    break;
            }

            AddGeometryModel(group, bodyMesh, body);
            AddGeometryModel(group, accentMesh, accent);
        }

        private static void AddGeometryModel(Model3DGroup group, MeshGeometry3D mesh, Material material)
        {
            if (mesh.Positions.Count == 0) return;
            group.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
        }

        private static MaterialGroup CreateLitMaterial(Color color, double glow)
        {
            var diffuseBrush = new SolidColorBrush(color);
            var diffuse = new DiffuseMaterial(diffuseBrush);
            var specular = new SpecularMaterial(new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)), 44);
            var group = new MaterialGroup();
            group.Children.Add(diffuse);
            group.Children.Add(specular);
            if (glow > 0)
            {
                byte alpha = (byte)Math.Max(0, Math.Min(255, Math.Round(glow * 255.0)));
                group.Children.Add(new EmissiveMaterial(
                    new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B))));
            }
            if (group.CanFreeze) group.Freeze();
            return group;
        }

        private static void AddTorus(
            MeshGeometry3D mesh,
            double cx,
            double cy,
            double cz,
            double majorRadius,
            double tubeRadius,
            int majorSegments,
            int tubeSegments,
            double yScale = 1.0,
            double phase = 0.0,
            int plane = 0)
        {
            majorSegments = Math.Max(3, majorSegments);
            tubeSegments = Math.Max(3, tubeSegments);
            int baseIndex = mesh.Positions.Count;
            for (int major = 0; major <= majorSegments; major++)
            {
                double theta = Math.PI * 2.0 * major / majorSegments + phase;
                double cosTheta = Math.Cos(theta);
                double sinTheta = Math.Sin(theta);
                for (int tube = 0; tube <= tubeSegments; tube++)
                {
                    double phi = Math.PI * 2.0 * tube / tubeSegments;
                    double ringRadius = majorRadius + tubeRadius * Math.Cos(phi);
                    double tubeDepth = tubeRadius * Math.Sin(phi);
                    Point3D point = plane switch
                    {
                        1 => new Point3D(
                            cx + tubeDepth,
                            cy + ringRadius * cosTheta * yScale,
                            cz + ringRadius * sinTheta),
                        2 => new Point3D(
                            cx + ringRadius * cosTheta,
                            cy + tubeDepth * yScale,
                            cz + ringRadius * sinTheta),
                        _ => new Point3D(
                            cx + ringRadius * cosTheta,
                            cy + ringRadius * sinTheta * yScale,
                            cz + tubeDepth)
                    };
                    mesh.Positions.Add(point);
                }
            }

            int stride = tubeSegments + 1;
            for (int major = 0; major < majorSegments; major++)
            {
                for (int tube = 0; tube < tubeSegments; tube++)
                {
                    int a = baseIndex + major * stride + tube;
                    int b = a + stride;
                    mesh.TriangleIndices.Add(a);
                    mesh.TriangleIndices.Add(b);
                    mesh.TriangleIndices.Add(a + 1);
                    mesh.TriangleIndices.Add(a + 1);
                    mesh.TriangleIndices.Add(b);
                    mesh.TriangleIndices.Add(b + 1);
                }
            }
        }

        private static void AddCylinderZ(
            MeshGeometry3D mesh,
            double cx,
            double cy,
            double z0,
            double z1,
            double radius,
            int segments)
        {
            segments = Math.Max(3, segments);
            for (int i = 0; i < segments; i++)
            {
                double a0 = Math.PI * 2.0 * i / segments;
                double a1 = Math.PI * 2.0 * (i + 1) / segments;
                var p0 = new Point3D(cx + Math.Cos(a0) * radius, cy + Math.Sin(a0) * radius, z0);
                var p1 = new Point3D(cx + Math.Cos(a1) * radius, cy + Math.Sin(a1) * radius, z0);
                var q0 = new Point3D(p0.X, p0.Y, z1);
                var q1 = new Point3D(p1.X, p1.Y, z1);
                AddQuad(mesh, p0, p1, q1, q0);
                AddTriangle(mesh, new Point3D(cx, cy, z0), p1, p0);
                AddTriangle(mesh, new Point3D(cx, cy, z1), q0, q1);
            }
        }

        private static void AddEllipsoid(
            MeshGeometry3D mesh,
            double cx,
            double cy,
            double cz,
            double rx,
            double ry,
            double rz,
            int slices,
            int stacks)
        {
            int baseIndex = mesh.Positions.Count;
            for (int stack = 0; stack <= stacks; stack++)
            {
                double phi = Math.PI * stack / stacks;
                double sinPhi = Math.Sin(phi);
                double cosPhi = Math.Cos(phi);
                for (int slice = 0; slice <= slices; slice++)
                {
                    double theta = Math.PI * 2.0 * slice / slices;
                    mesh.Positions.Add(new Point3D(
                        cx + rx * sinPhi * Math.Cos(theta),
                        cy + ry * cosPhi,
                        cz + rz * sinPhi * Math.Sin(theta)));
                }
            }
            for (int stack = 0; stack < stacks; stack++)
            {
                for (int slice = 0; slice < slices; slice++)
                {
                    int a = baseIndex + stack * (slices + 1) + slice;
                    int b = a + slices + 1;
                    mesh.TriangleIndices.Add(a);
                    mesh.TriangleIndices.Add(b);
                    mesh.TriangleIndices.Add(a + 1);
                    mesh.TriangleIndices.Add(a + 1);
                    mesh.TriangleIndices.Add(b);
                    mesh.TriangleIndices.Add(b + 1);
                }
            }
        }

        private static void AddCylinderY(
            MeshGeometry3D mesh,
            double cx,
            double y0,
            double cz,
            double ignoredCenterY,
            double y1,
            double radius,
            int segments)
        {
            _ = ignoredCenterY;
            for (int i = 0; i < segments; i++)
            {
                double a0 = Math.PI * 2.0 * i / segments;
                double a1 = Math.PI * 2.0 * (i + 1) / segments;
                var b0 = new Point3D(cx + Math.Cos(a0) * radius, y0, cz + Math.Sin(a0) * radius);
                var b1 = new Point3D(cx + Math.Cos(a1) * radius, y0, cz + Math.Sin(a1) * radius);
                var t0 = new Point3D(b0.X, y1, b0.Z);
                var t1 = new Point3D(b1.X, y1, b1.Z);
                AddQuad(mesh, b0, b1, t1, t0);
                AddTriangle(mesh, new Point3D(cx, y0, cz), b1, b0);
                AddTriangle(mesh, new Point3D(cx, y1, cz), t0, t1);
            }
        }

        private static void AddBeamPrism2D(
            MeshGeometry3D mesh,
            Point start,
            Point end,
            double halfWidth,
            double zNear,
            double zFar)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.0001) return;
            double nx = -dy / len * halfWidth;
            double ny = dx / len * halfWidth;
            Point3D a0 = new Point3D(start.X + nx, start.Y + ny, zNear);
            Point3D b0 = new Point3D(end.X + nx, end.Y + ny, zNear);
            Point3D c0 = new Point3D(end.X - nx, end.Y - ny, zNear);
            Point3D d0 = new Point3D(start.X - nx, start.Y - ny, zNear);
            Point3D a1 = new Point3D(a0.X, a0.Y, zFar);
            Point3D b1 = new Point3D(b0.X, b0.Y, zFar);
            Point3D c1 = new Point3D(c0.X, c0.Y, zFar);
            Point3D d1 = new Point3D(d0.X, d0.Y, zFar);
            AddQuad(mesh, a0, b0, c0, d0);
            AddQuad(mesh, d1, c1, b1, a1);
            AddQuad(mesh, a0, a1, b1, b0);
            AddQuad(mesh, b0, b1, c1, c0);
            AddQuad(mesh, c0, c1, d1, d0);
            AddQuad(mesh, d0, d1, a1, a0);
        }

        private static void AddTrianglePrism2D(
            MeshGeometry3D mesh,
            Point a,
            Point b,
            Point c,
            double zNear,
            double zFar)
        {
            var an = new Point3D(a.X, a.Y, zNear);
            var bn = new Point3D(b.X, b.Y, zNear);
            var cn = new Point3D(c.X, c.Y, zNear);
            var af = new Point3D(a.X, a.Y, zFar);
            var bf = new Point3D(b.X, b.Y, zFar);
            var cf = new Point3D(c.X, c.Y, zFar);
            AddTriangle(mesh, an, bn, cn);
            AddTriangle(mesh, cf, bf, af);
            AddQuad(mesh, an, af, bf, bn);
            AddQuad(mesh, bn, bf, cf, cn);
            AddQuad(mesh, cn, cf, af, an);
        }

        private static DiffuseMaterial CreateMaterial(Color color)
        {
            var brush = new SolidColorBrush(color);
            if (brush.CanFreeze) brush.Freeze();
            var material = new DiffuseMaterial(brush);
            if (material.CanFreeze) material.Freeze();
            return material;
        }

        private static void AddBox(MeshGeometry3D mesh, double x0, double y0, double z0, double x1, double y1, double z1)
        {
            AddQuad(mesh, new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), new Point3D(x1, y1, z0), new Point3D(x0, y1, z0));
            AddQuad(mesh, new Point3D(x1, y0, z1), new Point3D(x0, y0, z1), new Point3D(x0, y1, z1), new Point3D(x1, y1, z1));
            AddQuad(mesh, new Point3D(x0, y0, z1), new Point3D(x0, y0, z0), new Point3D(x0, y1, z0), new Point3D(x0, y1, z1));
            AddQuad(mesh, new Point3D(x1, y0, z0), new Point3D(x1, y0, z1), new Point3D(x1, y1, z1), new Point3D(x1, y1, z0));
            AddQuad(mesh, new Point3D(x0, y1, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1));
            AddQuad(mesh, new Point3D(x0, y0, z1), new Point3D(x1, y0, z1), new Point3D(x1, y0, z0), new Point3D(x0, y0, z0));
        }

        private static void AddTexturedBox(
            MeshGeometry3D mesh,
            double x0,
            double y0,
            double z0,
            double x1,
            double y1,
            double z1)
        {
            Point uvBL = new Point(0, 1);
            Point uvBR = new Point(1, 1);
            Point uvTR = new Point(1, 0);
            Point uvTL = new Point(0, 0);
            AddTexturedQuad(mesh,
                new Point3D(x0, y0, z1), new Point3D(x1, y0, z1),
                new Point3D(x1, y1, z1), new Point3D(x0, y1, z1),
                uvBL, uvBR, uvTR, uvTL);
            AddTexturedQuad(mesh,
                new Point3D(x1, y0, z0), new Point3D(x0, y0, z0),
                new Point3D(x0, y1, z0), new Point3D(x1, y1, z0),
                uvBL, uvBR, uvTR, uvTL);
            AddTexturedQuad(mesh,
                new Point3D(x0, y0, z0), new Point3D(x0, y0, z1),
                new Point3D(x0, y1, z1), new Point3D(x0, y1, z0),
                uvBL, uvBR, uvTR, uvTL);
            AddTexturedQuad(mesh,
                new Point3D(x1, y0, z1), new Point3D(x1, y0, z0),
                new Point3D(x1, y1, z0), new Point3D(x1, y1, z1),
                uvBL, uvBR, uvTR, uvTL);
            AddTexturedQuad(mesh,
                new Point3D(x0, y1, z1), new Point3D(x1, y1, z1),
                new Point3D(x1, y1, z0), new Point3D(x0, y1, z0),
                uvBL, uvBR, uvTR, uvTL);
            AddTexturedQuad(mesh,
                new Point3D(x0, y0, z0), new Point3D(x1, y0, z0),
                new Point3D(x1, y0, z1), new Point3D(x0, y0, z1),
                uvBL, uvBR, uvTR, uvTL);
        }

        private static void AddTexturedQuad(
            MeshGeometry3D mesh,
            Point3D a,
            Point3D b,
            Point3D c,
            Point3D d,
            Point ta,
            Point tb,
            Point tc,
            Point td)
        {
            int i = mesh.Positions.Count;
            mesh.Positions.Add(a);
            mesh.Positions.Add(b);
            mesh.Positions.Add(c);
            mesh.Positions.Add(d);
            mesh.TextureCoordinates.Add(ta);
            mesh.TextureCoordinates.Add(tb);
            mesh.TextureCoordinates.Add(tc);
            mesh.TextureCoordinates.Add(td);
            mesh.TriangleIndices.Add(i + 0);
            mesh.TriangleIndices.Add(i + 1);
            mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i + 0);
            mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i + 3);
        }

        private static void AddQuad(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c, Point3D d)
        {
            int i = mesh.Positions.Count;
            mesh.Positions.Add(a);
            mesh.Positions.Add(b);
            mesh.Positions.Add(c);
            mesh.Positions.Add(d);
            mesh.TriangleIndices.Add(i + 0);
            mesh.TriangleIndices.Add(i + 1);
            mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i + 0);
            mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i + 3);
        }

        private static void AddTriangle(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c)
        {
            int i = mesh.Positions.Count;
            mesh.Positions.Add(a);
            mesh.Positions.Add(b);
            mesh.Positions.Add(c);
            mesh.TriangleIndices.Add(i + 0);
            mesh.TriangleIndices.Add(i + 1);
            mesh.TriangleIndices.Add(i + 2);
        }

        private static void AddSpikePrism(MeshGeometry3D mesh, double xBaseLeft, double xBaseRight, double yBase, double xTip, double yTip, double zNear, double zFar)
        {
            var aNear = new Point3D(xBaseLeft, yBase, zNear);
            var bNear = new Point3D(xBaseRight, yBase, zNear);
            var cNear = new Point3D(xTip, yTip, zNear);

            var aFar = new Point3D(xBaseLeft, yBase, zFar);
            var bFar = new Point3D(xBaseRight, yBase, zFar);
            var cFar = new Point3D(xTip, yTip, zFar);

            AddTriangle(mesh, aNear, bNear, cNear);
            AddTriangle(mesh, bFar, aFar, cFar);
            AddQuad(mesh, aNear, bNear, bFar, aFar);
            AddQuad(mesh, bNear, cNear, cFar, bFar);
            AddQuad(mesh, cNear, aNear, aFar, cFar);
        }
    }
}
