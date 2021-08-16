// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osuTK;

namespace osu.Game.Screens.Play.HUD
{
    public class MatchScoreDisplay : CompositeDrawable
    {
        private const float bar_height = 18;
        private const float font_size = 50;

        public BindableInt Team1Score = new BindableInt();
        public BindableInt Team2Score = new BindableInt();

        protected MatchScoreCounter Score1Text;
        protected MatchScoreCounter Score2Text;

        private Drawable score1Bar;
        private Drawable score2Bar;

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            InternalChildren = new[]
            {
                new Box
                {
                    Name = "top bar red (static)",
                    RelativeSizeAxes = Axes.X,
                    Height = bar_height / 4,
                    Width = 0.5f,
                    Colour = colours.TeamColourRed,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopRight
                },
                new Box
                {
                    Name = "top bar blue (static)",
                    RelativeSizeAxes = Axes.X,
                    Height = bar_height / 4,
                    Width = 0.5f,
                    Colour = colours.TeamColourBlue,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopLeft
                },
                score1Bar = new Box
                {
                    Name = "top bar red",
                    RelativeSizeAxes = Axes.X,
                    Height = bar_height,
                    Width = 0,
                    Colour = colours.TeamColourRed,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopRight
                },
                score2Bar = new Box
                {
                    Name = "top bar blue",
                    RelativeSizeAxes = Axes.X,
                    Height = bar_height,
                    Width = 0,
                    Colour = colours.TeamColourBlue,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopLeft
                },
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = font_size + bar_height,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Children = new Drawable[]
                    {
                        Score1Text = new MatchScoreCounter
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre
                        },
                        Score2Text = new MatchScoreCounter
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre
                        },
                    }
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            Team1Score.BindValueChanged(_ => updateScores());
            Team2Score.BindValueChanged(_ => updateScores(), true);
        }

        private void updateScores()
        {
            Score1Text.Current.Value = Team1Score.Value;
            Score2Text.Current.Value = Team2Score.Value;

            int comparison = Team1Score.Value.CompareTo(Team2Score.Value);

            if (comparison > 0)
            {
                Score1Text.Winning = true;
                Score2Text.Winning = false;
            }
            else if (comparison < 0)
            {
                Score1Text.Winning = false;
                Score2Text.Winning = true;
            }
            else
            {
                Score1Text.Winning = false;
                Score2Text.Winning = false;
            }

            var winningBar = Team1Score.Value > Team2Score.Value ? score1Bar : score2Bar;
            var losingBar = Team1Score.Value <= Team2Score.Value ? score1Bar : score2Bar;

            var diff = Math.Max(Team1Score.Value, Team2Score.Value) - Math.Min(Team1Score.Value, Team2Score.Value);

            losingBar.ResizeWidthTo(0, 400, Easing.OutQuint);
            winningBar.ResizeWidthTo(Math.Min(0.4f, MathF.Pow(diff / 1500000f, 0.5f) / 2), 400, Easing.OutQuint);
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();
            Score1Text.X = -Math.Max(5 + Score1Text.DrawWidth / 2, score1Bar.DrawWidth);
            Score2Text.X = Math.Max(5 + Score2Text.DrawWidth / 2, score2Bar.DrawWidth);
        }

        protected class MatchScoreCounter : ScoreCounter
        {
            private OsuSpriteText displayedSpriteText;

            public MatchScoreCounter()
            {
                Margin = new MarginPadding { Top = bar_height, Horizontal = 10 };
            }

            public bool Winning
            {
                set => updateFont(value);
            }

            protected override OsuSpriteText CreateSpriteText() => base.CreateSpriteText().With(s =>
            {
                displayedSpriteText = s;
                displayedSpriteText.Spacing = new Vector2(-6);
                updateFont(false);
            });

            private void updateFont(bool winning)
                => displayedSpriteText.Font = winning
                    ? OsuFont.Torus.With(weight: FontWeight.Bold, size: font_size, fixedWidth: true)
                    : OsuFont.Torus.With(weight: FontWeight.Regular, size: font_size * 0.8f, fixedWidth: true);

            protected override LocalisableString FormatCount(double count) => count.ToLocalisableString(@"N0");
        }
    }
}
