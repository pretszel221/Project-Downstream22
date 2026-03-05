// SPDX-FileCopyrightText: 2021 moonheart08 <moonheart08@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 Chris V <HoofedEar@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 Kara <lunarautomaton6@gmail.com>
// SPDX-FileCopyrightText: 2022 Veritius <veritiusgaming@gmail.com>
// SPDX-FileCopyrightText: 2022 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 metalgearsloth <comedian_vs_clown@hotmail.com>
// SPDX-FileCopyrightText: 2022 wrexbe <81056464+wrexbe@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 Leon Friedrich <60421075+ElectroJr@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 taydeo <td12233a@gmail.com>
//
// SPDX-License-Identifier: MIT

using System;
using System.Text;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.EUI;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Eui;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;

namespace Content.Server.Administration.UI
{
    public sealed class AdminAnnounceEui : BaseEui
    {
        [Dependency] private readonly IAdminManager _adminManager = default!;
        [Dependency] private readonly IChatManager _chatManager = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;
        [Dependency] private readonly IAdminLogManager _adminLogger = default!;
        private const string DefaultAnnouncementSound = "/Audio/Announcements/announce.ogg";
        private const string AlertAnnouncementSound = "/Audio/Announcements/attention.ogg";
        private const string InterceptAnnouncementSound = "/Audio/Announcements/intercept.ogg";
        private const string MeteorsAnnouncementSound = "/Audio/Announcements/meteors.ogg";
        private const string RadiationAnnouncementSound = "/Audio/Announcements/radiation.ogg";
        private const string ShuttleCalledAnnouncementSound = "/Audio/Announcements/shuttlecalled.ogg";
        private const string PowerOnAnnouncementSound = "/Audio/Announcements/power_on.ogg";
        private const string EvilAnnouncementSound = "/Audio/Announcements/bloblarm.ogg";
        private const string MercenaryAnnouncementSound = "/Audio/Announcements/war.ogg";
        private const string SovietAnnouncementSound = "/Audio/Announcements/ion_storm.ogg";
        private const int MaxAnnouncerLength = 64;
        private const int MaxSenderLabelLength = 96;
        private const int MinBodyFontSize = 10;
        private const int MaxBodyFontSize = 18;
        private const int MinHeaderFontSize = 12;
        private const int MaxHeaderFontSize = 20;

        public AdminAnnounceEui()
        {
            IoCManager.InjectDependencies(this);
        }

        public override EuiStateBase GetNewState()
        {
            return new AdminAnnounceEuiState();
        }

        public override void Opened()
        {
            base.Opened();

            if (!HasModeratorAccess())
                Close();
        }

        public override void HandleMessage(EuiMessageBase msg)
        {
            base.HandleMessage(msg);

            if (!HasModeratorAccess())
            {
                Close();
                return;
            }

            switch (msg)
            {
                case AdminAnnounceEuiMsg.DoAnnounce doAnnounce:
                    var announcement = NormalizeInput(doAnnounce.Announcement, allowNewline: true);
                    var announcer = NormalizeInput(doAnnounce.Announcer, allowNewline: false);

                    if (announcement.Length == 0)
                        break;

                    var maxAnnouncementLength = Math.Max(1, _cfg.GetCVar(CCVars.ChatMaxAnnouncementLength));
                    if (announcement.Length > maxAnnouncementLength)
                        announcement = announcement[..maxAnnouncementLength];

                    if (announcer.Length > MaxAnnouncerLength)
                        announcer = announcer[..MaxAnnouncerLength];

                    var sound = ResolveAnnouncementSound(doAnnounce.Sound);
                    var color = ResolveAnnouncementColor(doAnnounce.Color);
                    var font = ResolveAnnouncementFont(doAnnounce.Font);

                    var bodySize = Math.Clamp(doAnnounce.FontSize, MinBodyFontSize, MaxBodyFontSize);
                    var headerSize = Math.Clamp(bodySize + 2, MinHeaderFontSize, MaxHeaderFontSize);
                    var senderLabel = doAnnounce.IncludeAnnouncementSuffix
                        ? $"{announcer} {Loc.GetString("admin-announce-header-suffix")}"
                        : announcer;

                    if (senderLabel.Length > MaxSenderLabelLength)
                        senderLabel = senderLabel[..MaxSenderLabelLength];

                    switch (doAnnounce.AnnounceType)
                    {
                        case AdminAnnounceType.Server:
                            _chatManager.DispatchServerAnnouncement(announcement, colorOverride: color);
                            break;
                        // TODO: Per-station announcement support
                        case AdminAnnounceType.Station:
                            DispatchStyledGlobalAnnouncement(announcement, senderLabel, sound, color, font, headerSize, bodySize);
                            break;
                        default:
                            Close();
                            return;
                    }

                    if (doAnnounce.CloseAfter)
                        Close();

                    break;
            }
        }


        private bool HasModeratorAccess()
        {
            return Player != null && _adminManager.HasAdminFlag(Player, AdminFlags.Moderator);
        }


        private static string ResolveAnnouncementSound(AdminAnnounceSound sound)
        {
            return sound switch
            {
                AdminAnnounceSound.Alert => AlertAnnouncementSound,
                AdminAnnounceSound.Intercept => InterceptAnnouncementSound,
                AdminAnnounceSound.Meteors => MeteorsAnnouncementSound,
                AdminAnnounceSound.Radiation => RadiationAnnouncementSound,
                AdminAnnounceSound.ShuttleCalled => ShuttleCalledAnnouncementSound,
                AdminAnnounceSound.PowerOn => PowerOnAnnouncementSound,
                AdminAnnounceSound.Evil => EvilAnnouncementSound,
                AdminAnnounceSound.Mercenary => MercenaryAnnouncementSound,
                AdminAnnounceSound.Soviet => SovietAnnouncementSound,
                _ => DefaultAnnouncementSound
            };
        }

        private static Color ResolveAnnouncementColor(AdminAnnounceColor color)
        {
            return color switch
            {
                AdminAnnounceColor.Purple => Color.MediumPurple,
                AdminAnnounceColor.Orange => Color.Orange,
                AdminAnnounceColor.Red => Color.IndianRed,
                AdminAnnounceColor.Cyan => Color.Cyan,
                AdminAnnounceColor.Blue => Color.DodgerBlue,
                AdminAnnounceColor.Green => Color.LightGreen,
                _ => Color.Gold,
            };
        }

        private static string ResolveAnnouncementFont(AdminAnnounceFont font)
        {
            return font switch
            {
                AdminAnnounceFont.Monospace => "Monospace",
                AdminAnnounceFont.BoxRound => "BoxRound",
                AdminAnnounceFont.AnimalSilence => "AnimalSilence",
                _ => "DefaultBold",
            };
        }

        private void DispatchStyledGlobalAnnouncement(
            string announcement,
            string announcer,
            string announcementSound,
            Color color,
            string font,
            int headerSize,
            int bodySize)
        {
            if (announcer.Length == 0)
                announcer = Loc.GetString("chat-manager-sender-announcement");

            var wrappedMessage = Loc.GetString(
                "admin-announce-styled-wrap-message",
                ("sender", FormattedMessage.EscapeText(announcer)),
                ("message", FormattedMessage.EscapeText(announcement)),
                ("font", font),
                ("headerSize", headerSize),
                ("bodySize", bodySize));

            _chatManager.ChatMessageToAll(ChatChannel.Radio,
                announcement,
                wrappedMessage,
                default,
                false,
                true,
                color,
                audioPath: announcementSound,
                audioVolume: -2f);

            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Global station announcement from {announcer}: {announcement}");
        }

        private static string NormalizeInput(string value, bool allowNewline)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            StringBuilder? sb = null;
            var newlineCount = 0;

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                var allowed = !char.IsControl(c) || (allowNewline && c == '\n');

                if (allowed)
                {
                    if (allowNewline && c == '\n')
                    {
                        newlineCount++;
                        if (newlineCount > 4)
                        {
                            sb ??= new StringBuilder(value.Length);
                            if (i > 0 && sb.Length == 0)
                                sb.Append(value, 0, i);

                            sb.Append(' ');
                            continue;
                        }
                    }

                    sb?.Append(c);
                    continue;
                }

                if (sb == null)
                {
                    sb = new StringBuilder(value.Length);
                    if (i > 0)
                        sb.Append(value, 0, i);
                }

                if (allowNewline && c == '\r')
                    continue;

                sb.Append(' ');
            }

            return (sb?.ToString() ?? value).Trim();
        }
    }
}
