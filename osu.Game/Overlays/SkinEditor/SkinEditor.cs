// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Testing;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Cursor;
using osu.Game.Graphics.UserInterface;
using osu.Game.Localisation;
using osu.Game.Overlays.OSD;
using osu.Game.Screens.Edit.Components;
using osu.Game.Screens.Edit.Components.Menus;
using osu.Game.Skinning;

namespace osu.Game.Overlays.SkinEditor
{
    [Cached(typeof(SkinEditor))]
    public partial class SkinEditor : VisibilityContainer, ICanAcceptFiles, IKeyBindingHandler<PlatformAction>
    {
        public const double TRANSITION_DURATION = 300;

        public const float MENU_HEIGHT = 40;

        public readonly BindableList<ISkinnableDrawable> SelectedComponents = new BindableList<ISkinnableDrawable>();

        protected override bool StartHidden => true;

        private Drawable targetScreen = null!;

        private OsuTextFlowContainer headerText = null!;

        private Bindable<Skin> currentSkin = null!;

        [Resolved]
        private OsuGame? game { get; set; }

        [Resolved]
        private SkinManager skins { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private SkinEditorOverlay? skinEditorOverlay { get; set; }

        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Blue);

        private bool hasBegunMutating;

        private Container? content;

        private EditorSidebar componentsSidebar = null!;
        private EditorSidebar settingsSidebar = null!;

        [Resolved]
        private OnScreenDisplay? onScreenDisplay { get; set; }

        public SkinEditor()
        {
        }

        public SkinEditor(Drawable targetScreen)
        {
            UpdateTargetScreen(targetScreen);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChild = new OsuContextMenuContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    RowDimensions = new[]
                    {
                        new Dimension(GridSizeMode.AutoSize),
                        new Dimension(GridSizeMode.AutoSize),
                        new Dimension(),
                    },

                    Content = new[]
                    {
                        new Drawable[]
                        {
                            new Container
                            {
                                Name = @"Menu container",
                                RelativeSizeAxes = Axes.X,
                                Depth = float.MinValue,
                                Height = MENU_HEIGHT,
                                Children = new Drawable[]
                                {
                                    new EditorMenuBar
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        RelativeSizeAxes = Axes.Both,
                                        Items = new[]
                                        {
                                            new MenuItem(CommonStrings.MenuBarFile)
                                            {
                                                Items = new[]
                                                {
                                                    new EditorMenuItem(Resources.Localisation.Web.CommonStrings.ButtonsSave, MenuItemType.Standard, () => Save()),
                                                    new EditorMenuItem(CommonStrings.RevertToDefault, MenuItemType.Destructive, revert),
                                                    new EditorMenuItemSpacer(),
                                                    new EditorMenuItem(CommonStrings.Exit, MenuItemType.Standard, () => skinEditorOverlay?.Hide()),
                                                },
                                            },
                                        }
                                    },
                                    headerText = new OsuTextFlowContainer
                                    {
                                        TextAnchor = Anchor.TopRight,
                                        Padding = new MarginPadding(5),
                                        Anchor = Anchor.TopRight,
                                        Origin = Anchor.TopRight,
                                        AutoSizeAxes = Axes.X,
                                        RelativeSizeAxes = Axes.Y,
                                    },
                                },
                            },
                        },
                        new Drawable[]
                        {
                            new SkinEditorSceneLibrary
                            {
                                RelativeSizeAxes = Axes.X,
                            },
                        },
                        new Drawable[]
                        {
                            new GridContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                ColumnDimensions = new[]
                                {
                                    new Dimension(GridSizeMode.AutoSize),
                                    new Dimension(),
                                    new Dimension(GridSizeMode.AutoSize),
                                },
                                Content = new[]
                                {
                                    new Drawable[]
                                    {
                                        componentsSidebar = new EditorSidebar(),
                                        content = new Container
                                        {
                                            Depth = float.MaxValue,
                                            RelativeSizeAxes = Axes.Both,
                                        },
                                        settingsSidebar = new EditorSidebar(),
                                    }
                                }
                            }
                        },
                    }
                }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            Show();

            game?.RegisterImportHandler(this);

            // as long as the skin editor is loaded, let's make sure we can modify the current skin.
            currentSkin = skins.CurrentSkin.GetBoundCopy();

            // schedule ensures this only happens when the skin editor is visible.
            // also avoid some weird endless recursion / bindable feedback loop (something to do with tracking skins across three different bindable types).
            // probably something which will be factored out in a future database refactor so not too concerning for now.
            currentSkin.BindValueChanged(_ =>
            {
                hasBegunMutating = false;
                Scheduler.AddOnce(skinChanged);
            }, true);

            SelectedComponents.BindCollectionChanged((_, _) => Scheduler.AddOnce(populateSettings), true);
        }

        public bool OnPressed(KeyBindingPressEvent<PlatformAction> e)
        {
            switch (e.Action)
            {
                case PlatformAction.Save:
                    if (e.Repeat)
                        return false;

                    Save();
                    return true;
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<PlatformAction> e)
        {
        }

        public void UpdateTargetScreen(Drawable targetScreen)
        {
            this.targetScreen = targetScreen;

            SelectedComponents.Clear();

            // Immediately clear the previous blueprint container to ensure it doesn't try to interact with the old target.
            content?.Clear();

            Scheduler.AddOnce(loadBlueprintContainer);
            Scheduler.AddOnce(populateSettings);

            void loadBlueprintContainer()
            {
                Debug.Assert(content != null);

                content.Child = new SkinBlueprintContainer(targetScreen);

                componentsSidebar.Child = new SkinComponentToolbox(getFirstTarget() as CompositeDrawable)
                {
                    RequestPlacement = placeComponent
                };
            }
        }

        private void skinChanged()
        {
            headerText.Clear();

            headerText.AddParagraph(SkinEditorStrings.SkinEditor, cp => cp.Font = OsuFont.Default.With(size: 16));
            headerText.NewParagraph();
            headerText.AddText(SkinEditorStrings.CurrentlyEditing, cp =>
            {
                cp.Font = OsuFont.Default.With(size: 12);
                cp.Colour = colours.Yellow;
            });

            headerText.AddText($" {currentSkin.Value.SkinInfo}", cp =>
            {
                cp.Font = OsuFont.Default.With(size: 12, weight: FontWeight.Bold);
                cp.Colour = colours.Yellow;
            });

            skins.EnsureMutableSkin();
            hasBegunMutating = true;
        }

        private void placeComponent(Type type)
        {
            if (!(Activator.CreateInstance(type) is ISkinnableDrawable component))
                throw new InvalidOperationException($"Attempted to instantiate a component for placement which was not an {typeof(ISkinnableDrawable)}.");

            placeComponent(component);
        }

        private void placeComponent(ISkinnableDrawable component, bool applyDefaults = true)
        {
            var targetContainer = getFirstTarget();

            if (targetContainer == null)
                return;

            var drawableComponent = (Drawable)component;

            if (applyDefaults)
            {
                // give newly added components a sane starting location.
                drawableComponent.Origin = Anchor.TopCentre;
                drawableComponent.Anchor = Anchor.TopCentre;
                drawableComponent.Y = targetContainer.DrawSize.Y / 2;
            }

            targetContainer.Add(component);

            SelectedComponents.Clear();
            SelectedComponents.Add(component);
        }

        private void populateSettings()
        {
            settingsSidebar.Clear();

            foreach (var component in SelectedComponents.OfType<Drawable>())
                settingsSidebar.Add(new SkinSettingsToolbox(component));
        }

        private IEnumerable<ISkinnableTarget> availableTargets => targetScreen.ChildrenOfType<ISkinnableTarget>();

        private ISkinnableTarget? getFirstTarget() => availableTargets.FirstOrDefault();

        private ISkinnableTarget? getTarget(GlobalSkinComponentLookup.LookupType target)
        {
            return availableTargets.FirstOrDefault(c => c.Target == target);
        }

        private void revert()
        {
            ISkinnableTarget[] targetContainers = availableTargets.ToArray();

            foreach (var t in targetContainers)
            {
                currentSkin.Value.ResetDrawableTarget(t);

                // add back default components
                getTarget(t.Target)?.Reload();
            }
        }

        public void Save(bool userTriggered = true)
        {
            if (!hasBegunMutating)
                return;

            ISkinnableTarget[] targetContainers = availableTargets.ToArray();

            foreach (var t in targetContainers)
                currentSkin.Value.UpdateDrawableTarget(t);

            // In the case the save was user triggered, always show the save message to make them feel confident.
            if (skins.Save(skins.CurrentSkin.Value) || userTriggered)
                onScreenDisplay?.Display(new SkinEditorToast(ToastStrings.SkinSaved, currentSkin.Value.SkinInfo.ToString() ?? "Unknown"));
        }

        protected override bool OnHover(HoverEvent e) => true;

        protected override bool OnMouseDown(MouseDownEvent e) => true;

        public override void Hide()
        {
            base.Hide();
            SelectedComponents.Clear();
        }

        protected override void PopIn()
        {
            this.FadeIn(TRANSITION_DURATION, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            this.FadeOut(TRANSITION_DURATION, Easing.OutQuint);
        }

        public void DeleteItems(ISkinnableDrawable[] items)
        {
            foreach (var item in items)
                availableTargets.FirstOrDefault(t => t.Components.Contains(item))?.Remove(item);
        }

        #region Drag & drop import handling

        public Task Import(params string[] paths)
        {
            Schedule(() =>
            {
                var file = new FileInfo(paths.First());

                // import to skin
                currentSkin.Value.SkinInfo.PerformWrite(skinInfo =>
                {
                    using (var contents = file.OpenRead())
                        skins.AddFile(skinInfo, contents, file.Name);
                });

                // Even though we are 100% on an update thread, we need to wait for realm callbacks to fire (to correctly invalidate caches in RealmBackedResourceStore).
                // See https://github.com/realm/realm-dotnet/discussions/2634#discussioncomment-2483573 for further discussion.
                // This is the best we can do for now.
                realm.Run(r => r.Refresh());

                var skinnableTarget = getFirstTarget();

                // Import still should happen for now, even if not placeable (as it allows a user to import skin resources that would apply to legacy gameplay skins).
                if (skinnableTarget == null)
                    return;

                // place component
                var sprite = new SkinnableSprite
                {
                    SpriteName = { Value = file.Name },
                    Origin = Anchor.Centre,
                    Position = skinnableTarget.ToLocalSpace(GetContainingInputManager().CurrentState.Mouse.Position),
                };

                placeComponent(sprite, false);

                SkinSelectionHandler.ApplyClosestAnchor(sprite);
            });

            return Task.CompletedTask;
        }

        Task ICanAcceptFiles.Import(ImportTask[] tasks, ImportParameters parameters) => throw new NotImplementedException();

        public IEnumerable<string> HandledExtensions => new[] { ".jpg", ".jpeg", ".png" };

        #endregion

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            game?.UnregisterImportHandler(this);
        }

        private partial class SkinEditorToast : Toast
        {
            public SkinEditorToast(LocalisableString value, string skinDisplayName)
                : base(SkinSettingsStrings.SkinLayoutEditor, value, skinDisplayName)
            {
            }
        }
    }
}
