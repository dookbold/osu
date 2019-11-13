﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.UserInterface;
using osuTK;
using System.Linq;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Beatmaps;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Framework.Bindables;
using osu.Game.Rulesets;
using osu.Game.Screens.Select.Leaderboards;
using osu.Game.Users;

namespace osu.Game.Overlays.BeatmapSet.Scores
{
    public class ScoresContainer : CompositeDrawable
    {
        private const int spacing = 15;
        private const int duration = 200;

        public readonly Bindable<BeatmapInfo> Beatmap = new Bindable<BeatmapInfo>();
        private readonly Bindable<RulesetInfo> ruleset = new Bindable<RulesetInfo>();
        private readonly Bindable<BeatmapLeaderboardScope> scope = new Bindable<BeatmapLeaderboardScope>();
        private readonly Bindable<User> user = new Bindable<User>();

        private readonly Box background;
        private readonly ScoreTable scoreTable;
        private readonly FillFlowContainer topScoresContainer;
        private readonly DimmedLoadingLayer loading;
        private readonly FillFlowContainer modFilter;
        private readonly LeaderboardModSelector modSelector;
        private readonly NoScoresPlaceholder noScoresPlaceholder;
        private readonly FillFlowContainer content;

        [Resolved]
        private IAPIProvider api { get; set; }

        private GetScoresRequest getScoresRequest;

        protected APILegacyScores Scores
        {
            set
            {
                Schedule(() =>
                {
                    topScoresContainer.Clear();

                    if (value?.Scores.Any() != true)
                    {
                        scoreTable.Scores = null;
                        scoreTable.Hide();
                        return;
                    }

                    scoreTable.Scores = value.Scores;
                    scoreTable.Show();

                    var topScore = value.Scores.First();
                    var userScore = value.UserScore;

                    topScoresContainer.Add(new DrawableTopScore(topScore));

                    if (userScore != null && userScore.Score.OnlineScoreID != topScore.OnlineScoreID)
                        topScoresContainer.Add(new DrawableTopScore(userScore.Score, userScore.Position));
                });
            }
        }

        public ScoresContainer()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            InternalChildren = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                },
                content = new FillFlowContainer
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Width = 0.95f,
                    Direction = FillDirection.Vertical,
                    Margin = new MarginPadding { Vertical = spacing },
                    Children = new Drawable[]
                    {
                        modFilter = new FillFlowContainer
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, spacing),
                            Children = new Drawable[]
                            {
                                new LeaderboardScopeSelector
                                {
                                    Current = { BindTarget = scope }
                                },
                                modSelector = new LeaderboardModSelector
                                {
                                    Ruleset = { BindTarget = ruleset }
                                }
                            }
                        },
                        new Container
                        {
                            AutoSizeAxes = Axes.Y,
                            RelativeSizeAxes = Axes.X,
                            Margin = new MarginPadding { Vertical = spacing },
                            Children = new Drawable[]
                            {
                                noScoresPlaceholder = new NoScoresPlaceholder
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    Alpha = 0,
                                    AlwaysPresent = true,
                                    Margin = new MarginPadding { Vertical = 10 }
                                },
                                new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Vertical,
                                    Spacing = new Vector2(0, spacing),
                                    Children = new Drawable[]
                                    {
                                        topScoresContainer = new FillFlowContainer
                                        {
                                            RelativeSizeAxes = Axes.X,
                                            AutoSizeAxes = Axes.Y,
                                            Direction = FillDirection.Vertical,
                                            Spacing = new Vector2(0, 5),
                                        },
                                        scoreTable = new ScoreTable
                                        {
                                            Anchor = Anchor.TopCentre,
                                            Origin = Anchor.TopCentre,
                                        }
                                    }
                                },
                                new Container
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Masking = true,
                                    CornerRadius = 10,
                                    Child = loading = new DimmedLoadingLayer(iconScale: 0.8f)
                                    {
                                        Alpha = 0,
                                    },
                                }
                            }
                        }
                    }
                },
            };
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            background.Colour = colours.Gray2;

            user.BindTo(api.LocalUser);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            scope.BindValueChanged(_ => getScores());
            ruleset.BindValueChanged(_ => getScores());

            modSelector.SelectedMods.ItemsAdded += _ => getScores();
            modSelector.SelectedMods.ItemsRemoved += _ => getScores();

            Beatmap.BindValueChanged(onBeatmapChanged);
            user.BindValueChanged(onUserChanged, true);
        }

        private void onBeatmapChanged(ValueChangedEvent<BeatmapInfo> beatmap)
        {
            var beatmapRuleset = beatmap.NewValue?.Ruleset;

            if (ruleset.Value?.Equals(beatmapRuleset) ?? false)
                modSelector.DeselectAll();
            else
                ruleset.Value = beatmapRuleset;

            scope.Value = BeatmapLeaderboardScope.Global;
        }

        private void onUserChanged(ValueChangedEvent<User> user)
        {
            scope.Value = BeatmapLeaderboardScope.Global;
            modSelector.DeselectAll();
            updateModFilterVisibility();
        }

        private void updateModFilterVisibility()
        {
            modFilter.FadeTo(api.IsLoggedIn && api.LocalUser.Value.IsSupporter && !hasNoLeaderboard ? 1 : 0);
        }

        private void getScores()
        {
            getScoresRequest?.Cancel();
            getScoresRequest = null;

            noScoresPlaceholder.FadeOut(duration, Easing.OutQuint);

            updateModFilterVisibility();

            if (hasNoLeaderboard)
            {
                Scores = null;
                content.Hide();
                return;
            }

            content.Show();
            loading.Show();
            getScoresRequest = new GetScoresRequest(Beatmap.Value, Beatmap.Value.Ruleset, scope.Value, modSelector.SelectedMods);
            getScoresRequest.Success += scores =>
            {
                loading.Hide();
                Scores = scores;

                if (!scores.Scores.Any())
                {
                    noScoresPlaceholder.UpdateText(scope.Value);
                    noScoresPlaceholder.FadeIn(duration, Easing.OutQuint);
                }
            };
            api.Queue(getScoresRequest);
        }

        private bool hasNoLeaderboard => Beatmap.Value?.OnlineBeatmapID.HasValue != true || Beatmap.Value.Status <= BeatmapSetOnlineStatus.Pending;
    }
}
