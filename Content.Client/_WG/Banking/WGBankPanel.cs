using System.Globalization;
using System.Numerics;
using System.Text;
using Content.Shared._WG.Banking;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WG.Banking;

/// <summary>The Bank app: the account card with its round allowance, and pages to withdraw, transfer, deposit and read activity.</summary>
public sealed class WGBankPanel : BoxContainer
{
    private static readonly Color Backdrop = Color.FromHex("#0e131a");
    private static readonly Color CardColor = Color.FromHex("#162a40");
    private static readonly Color Surface = Color.FromHex("#151c25");
    private static readonly Color Field = Color.FromHex("#0b1017");
    private static readonly Color RowShade = Color.FromHex("#ffffff09");
    private static readonly Color Gold = Color.FromHex("#e6c46a");
    private static readonly Color GoldDark = Color.FromHex("#a88a3a");
    private static readonly Color Bright = Color.FromHex("#eef2f5");
    private static readonly Color Dim = Color.FromHex("#8a97a6");
    private static readonly Color Good = Color.FromHex("#7fd18b");
    private static readonly Color Bad = Color.FromHex("#e07a70");

    /// <summary>The quick-amount chips under each amount field, before Max.</summary>
    private static readonly int[] Quick = { 1_000, 5_000, 10_000, 50_000, 100_000, 250_000 };

    private enum Page
    {
        Withdraw,
        Transfer,
        Deposit,
        Activity,
    }

    public event Action<WGBankAction>? OnAction;

    private readonly Font _smallFont;
    private readonly Font _nameFont;
    private readonly Font _balanceFont;
    private readonly Font _monoFont;

    private WGBankUiState? _state;

    // Signed out.
    private readonly PanelContainer _signedOut = new() { VerticalExpand = true, HorizontalExpand = true };
    private readonly Label _signedOutTitle = new() { HorizontalAlignment = HAlignment.Center };
    private readonly RichTextLabel _signedOutText = new() { HorizontalAlignment = HAlignment.Center, MaxWidth = 380 };

    // The card.
    private readonly BoxContainer _account = new() { Orientation = LayoutOrientation.Vertical, VerticalExpand = true, HorizontalExpand = true };
    private readonly Label _holder = new() { ClipText = true };
    private readonly Label _details = new() { ClipText = true };
    private readonly Label _balance = new();
    private readonly Label _allowance = new();
    private readonly StyleBoxFlat _barFill = new() { BackgroundColor = Good };
    private readonly ProgressBar _bar = new() { MinHeight = 6, MinValue = 0, MaxValue = 1, Value = 1 };
    private readonly Label _locked = new() { Visible = false };

    private readonly Dictionary<Page, Button> _tabs = new();
    private readonly Dictionary<Page, Control> _pages = new();

    // Withdraw.
    private readonly AmountField _withdrawAmount;
    private readonly Label _withdrawBalanceAfter = new();
    private readonly Label _withdrawAllowanceAfter = new();
    private readonly Label _withdrawProblem = new();
    private readonly ConfirmButton _withdraw = new();

    // Transfer.
    private readonly LineEdit _recipient = new() { HorizontalExpand = true };
    private readonly AmountField _transferAmount;
    private readonly Label _transferTo = new() { ClipText = true };
    private readonly Label _transferAllowanceAfter = new();
    private readonly Label _transferProblem = new();
    private readonly ConfirmButton _transfer = new();

    // Deposit.
    private readonly Label _handCash = new() { HorizontalAlignment = HAlignment.Center };
    private readonly Label _depositHint = new() { HorizontalAlignment = HAlignment.Center, Align = Label.AlignMode.Center };
    private readonly ConfirmButton _deposit = new();

    // Activity.
    private readonly BoxContainer _history = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
    private string _historyKey = string.Empty;

    public WGBankPanel()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        _smallFont = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 9);
        _nameFont = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 13);
        _balanceFont = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 20);
        _monoFont = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Regular.ttf"), 10);

        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new BoxContainer { Orientation = LayoutOrientation.Vertical, VerticalExpand = true, HorizontalExpand = true, Margin = new Thickness(6) };
        AddChild(Panel(root, Backdrop, null, default, default, expand: true));

        _withdrawAmount = new AmountField(this);
        _transferAmount = new AmountField(this);

        BuildSignedOut();
        root.AddChild(_signedOut);

        _account.AddChild(BuildCard());
        _account.AddChild(BuildTabs());
        var pages = new BoxContainer { Orientation = LayoutOrientation.Vertical, VerticalExpand = true, HorizontalExpand = true };
        _pages[Page.Withdraw] = BuildWithdraw();
        _pages[Page.Transfer] = BuildTransfer();
        _pages[Page.Deposit] = BuildDeposit();
        _pages[Page.Activity] = BuildActivity();
        foreach (var page in _pages.Values)
        {
            pages.AddChild(page);
        }

        _account.AddChild(Panel(pages, Surface, null, default, new Thickness(8), expand: true));
        root.AddChild(_account);

        _account.Visible = false;
        SetPage(Page.Withdraw);
    }

    public void UpdateState(WGBankUiState state)
    {
        var switched = _state == null || _state.Holder != state.Holder || _state.Number != state.Number;
        _state = state;

        var open = state.Status is WGBankStatus.Ready or WGBankStatus.Locked;
        _signedOut.Visible = !open;
        _account.Visible = open;
        if (!open)
        {
            var key = state.Status.ToString().ToLowerInvariant();
            _signedOutTitle.Text = Loc.GetString($"wg-bank-status-{key}-title");
            _signedOutText.SetMessage(Colored(Loc.GetString($"wg-bank-status-{key}-text"), Dim));
            return;
        }

        if (switched)
        {
            // A different card: start clean rather than carry amounts or a payee over.
            _withdrawAmount.Set(0);
            _transferAmount.Set(0);
            _recipient.Text = string.Empty;
            _withdraw.Disarm();
            _transfer.Disarm();
            _deposit.Disarm();
        }

        _holder.Text = state.Holder;
        _details.Text = state.Job.Length > 0
            ? Loc.GetString("wg-bank-card-details", ("job", state.Job), ("number", state.Number))
            : Loc.GetString("wg-bank-card-number", ("number", state.Number));
        _balance.Text = Format(state.Balance);

        var left = Math.Max(0, state.Limit - state.Withdrawn);
        _allowance.Text = Loc.GetString("wg-bank-allowance-left", ("left", Format(left)), ("limit", Format(state.Limit)));
        _bar.MaxValue = Math.Max(1, state.Limit);
        _bar.Value = left;
        var share = state.Limit > 0 ? left / (float) state.Limit : 0f;
        _barFill.BackgroundColor = share < 0.15f ? Bad : share < 0.5f ? Gold : Good;
        _locked.Visible = state.Status == WGBankStatus.Locked;

        UpdateHistory();
        Refresh();
    }

    private void SetPage(Page page)
    {
        foreach (var (key, button) in _tabs)
        {
            button.Pressed = key == page;
        }

        foreach (var (key, control) in _pages)
        {
            control.Visible = key == page;
        }

        _withdraw.Disarm();
        _transfer.Disarm();
        _deposit.Disarm();
    }

    private void BuildSignedOut()
    {
        var column = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
        };
        column.AddChild(Brand(HAlignment.Center));
        _signedOutTitle.FontOverride = _nameFont;
        _signedOutTitle.FontColorOverride = Bright;
        _signedOutTitle.Margin = new Thickness(0, 10, 0, 4);
        column.AddChild(_signedOutTitle);
        column.AddChild(_signedOutText);

        _signedOut.PanelOverride = Box(CardColor, Gold, new Thickness(0, 2, 0, 0), new Thickness(16));
        _signedOut.AddChild(column);
    }

    /// <summary>The bank's mark: a gold card chip and the name.</summary>
    private Control Brand(HAlignment alignment)
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalAlignment = alignment };
        row.AddChild(new PanelContainer
        {
            MinSize = new Vector2(20, 14),
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            PanelOverride = new StyleBoxFlat { BackgroundColor = Gold, BorderColor = GoldDark, BorderThickness = new Thickness(1) },
        });
        row.AddChild(new Label { Text = Loc.GetString("wg-bank-brand"), FontOverride = _smallFont, FontColorOverride = Gold });
        return row;
    }

    private Control BuildCard()
    {
        var card = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };

        var top = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
        var who = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
        who.AddChild(Brand(HAlignment.Left));
        _holder.FontOverride = _nameFont;
        _holder.FontColorOverride = Bright;
        _holder.Margin = new Thickness(0, 4, 0, 0);
        who.AddChild(_holder);
        _details.FontOverride = _monoFont;
        _details.FontColorOverride = Dim;
        who.AddChild(_details);
        top.AddChild(who);

        var money = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalAlignment = HAlignment.Right };
        money.AddChild(new Label
        {
            Text = Loc.GetString("wg-bank-available"),
            FontOverride = _smallFont,
            FontColorOverride = Dim,
            HorizontalAlignment = HAlignment.Right,
        });
        var amount = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalAlignment = HAlignment.Right };
        _balance.FontOverride = _balanceFont;
        _balance.FontColorOverride = Gold;
        amount.AddChild(_balance);
        amount.AddChild(new Label
        {
            Text = Loc.GetString("wg-bank-currency"),
            FontColorOverride = Dim,
            VerticalAlignment = VAlignment.Bottom,
            Margin = new Thickness(5, 0, 0, 3),
        });
        money.AddChild(amount);
        top.AddChild(money);
        card.AddChild(top);

        var head = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 8, 0, 3) };
        head.AddChild(new Label
        {
            Text = Loc.GetString("wg-bank-allowance"),
            FontOverride = _smallFont,
            FontColorOverride = Dim,
            HorizontalExpand = true,
            ToolTip = Loc.GetString("wg-bank-allowance-tooltip"),
            MouseFilter = MouseFilterMode.Stop,
        });
        _allowance.FontColorOverride = Bright;
        head.AddChild(_allowance);
        card.AddChild(head);

        _bar.BackgroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Field };
        _bar.ForegroundStyleBoxOverride = _barFill;
        card.AddChild(_bar);

        _locked.Text = Loc.GetString("wg-bank-locked");
        _locked.FontColorOverride = Bad;
        _locked.Margin = new Thickness(0, 4, 0, 0);
        card.AddChild(_locked);

        return Panel(card, CardColor, Gold, new Thickness(3, 0, 0, 0), new Thickness(10, 8));
    }

    private Control BuildTabs()
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 6, 0, 4) };
        var pages = Enum.GetValues<Page>();
        for (var i = 0; i < pages.Length; i++)
        {
            var page = pages[i];
            var button = new Button
            {
                ToggleMode = true,
                HorizontalExpand = true,
                Text = Loc.GetString($"wg-bank-tab-{page.ToString().ToLowerInvariant()}"),
            };
            button.StyleClasses.Add(i == 0 ? "OpenRight" : i == pages.Length - 1 ? "OpenLeft" : "OpenBoth");
            button.OnPressed += _ => SetPage(page);
            _tabs[page] = button;
            row.AddChild(button);
        }

        return row;
    }

    private Control BuildWithdraw()
    {
        var left = Column();
        left.AddChild(Section("wg-bank-section-amount"));
        left.AddChild(_withdrawAmount);
        left.AddChild(Hint("wg-bank-withdraw-hint"));

        var right = Column();
        right.AddChild(Section("wg-bank-section-summary"));
        right.AddChild(Summary(
            Row("wg-bank-summary-balance-after", _withdrawBalanceAfter),
            Row("wg-bank-summary-allowance-after", _withdrawAllowanceAfter)));
        right.AddChild(Problem(_withdrawProblem));
        right.AddChild(new Control { VerticalExpand = true });
        _withdraw.Confirmed += () =>
        {
            if (_withdrawAmount.Amount is not { } amount)
                return;

            OnAction?.Invoke(new WGBankWithdrawAction { Amount = amount });
            _withdrawAmount.Set(0);
        };
        right.AddChild(_withdraw);
        return Columns(left, right);
    }

    private Control BuildTransfer()
    {
        var left = Column();
        left.AddChild(Section("wg-bank-section-recipient"));
        _recipient.PlaceHolder = Loc.GetString("wg-bank-recipient-placeholder");
        _recipient.OnTextChanged += _ =>
        {
            _transfer.Disarm();
            Refresh();
        };
        left.AddChild(_recipient);
        var amount = Section("wg-bank-section-amount");
        amount.Margin = new Thickness(0, 8, 0, 3);
        left.AddChild(amount);
        left.AddChild(_transferAmount);

        var right = Column();
        right.AddChild(Section("wg-bank-section-summary"));
        right.AddChild(Summary(
            Row("wg-bank-summary-to", _transferTo),
            Row("wg-bank-summary-allowance-after", _transferAllowanceAfter)));
        right.AddChild(Problem(_transferProblem));
        right.AddChild(new Control { VerticalExpand = true });
        _transfer.Confirmed += () =>
        {
            var recipient = _recipient.Text.Trim();
            if (_transferAmount.Amount is not { } sent || recipient.Length == 0)
                return;

            OnAction?.Invoke(new WGBankTransferAction { Recipient = recipient, Amount = sent });
            _transferAmount.Set(0);
        };
        right.AddChild(_transfer);
        return Columns(left, right);
    }

    private Control BuildDeposit()
    {
        var column = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            MinWidth = 280,
        };
        var inHand = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
        inHand.AddChild(new Label
        {
            Text = Loc.GetString("wg-bank-deposit-in-hand"),
            FontOverride = _smallFont,
            FontColorOverride = Dim,
            HorizontalAlignment = HAlignment.Center,
        });
        _handCash.FontOverride = _balanceFont;
        inHand.AddChild(_handCash);
        column.AddChild(Panel(inHand, Field, Gold, new Thickness(0, 0, 0, 2), new Thickness(12, 8)));

        _depositHint.FontColorOverride = Dim;
        _depositHint.Margin = new Thickness(0, 6);
        column.AddChild(_depositHint);
        _deposit.Confirmed += () => OnAction?.Invoke(new WGBankDepositAction());
        column.AddChild(_deposit);
        return column;
    }

    private Control BuildActivity()
    {
        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_history);
        return scroll;
    }

    /// <summary>Re-reads the typed amounts against the account: previews, problems and whether each button may be pressed.</summary>
    private void Refresh()
    {
        if (_state is not { } state)
            return;

        var ready = state.Status == WGBankStatus.Ready;
        var left = Math.Max(0, state.Limit - state.Withdrawn);

        var withdraw = _withdrawAmount.Amount;
        _withdrawBalanceAfter.Text = Money(Math.Max(0, state.Balance - (withdraw ?? 0)));
        _withdrawAllowanceAfter.Text = Money(Math.Max(0, left - (withdraw ?? 0)));
        var withdrawProblem = ProblemText(withdraw, state, left);
        _withdrawProblem.Text = withdrawProblem ?? string.Empty;
        _withdraw.Set(withdraw is { } w
                ? Loc.GetString("wg-bank-withdraw-button", ("amount", Format(w)))
                : Loc.GetString("wg-bank-withdraw-button-empty"),
            ready && withdraw != null && withdrawProblem == null);

        var transfer = _transferAmount.Amount;
        var recipient = _recipient.Text.Trim();
        var named = recipient.Length > 0;
        _transferTo.Text = named ? recipient : Loc.GetString("wg-bank-summary-to-none");
        _transferTo.FontColorOverride = named ? Bright : Dim;
        _transferAllowanceAfter.Text = Money(Math.Max(0, left - (transfer ?? 0)));
        var transferProblem = !state.DepositEnabled
            ? Loc.GetString("wg-bank-error-transfers-off")
            : ProblemText(transfer, state, left);
        _transferProblem.Text = transferProblem ?? string.Empty;
        _transfer.Set(transfer is { } t && named
                ? Loc.GetString("wg-bank-transfer-button", ("amount", Format(t)), ("to", recipient))
                : Loc.GetString("wg-bank-transfer-button-empty"),
            ready && transfer != null && named && transferProblem == null);

        _handCash.Text = state.HandCash > 0 ? Money(state.HandCash) : Loc.GetString("wg-bank-deposit-none");
        _handCash.FontColorOverride = state.HandCash > 0 ? Good : Dim;
        var depositLeft = Math.Max(0, state.DepositLimit - state.Deposited);
        var deposit = Math.Min(state.HandCash, depositLeft);
        _depositHint.Text = !state.DepositEnabled
            ? Loc.GetString("wg-bank-error-deposits-off")
            : depositLeft == 0
                ? Loc.GetString("wg-bank-error-deposit-limit")
                : Loc.GetString("wg-bank-deposit-hint", ("left", Format(depositLeft)), ("limit", Format(state.DepositLimit)));
        _deposit.Set(deposit > 0
                ? Loc.GetString("wg-bank-deposit-button", ("amount", Format(deposit)))
                : Loc.GetString("wg-bank-deposit-button-empty"),
            ready && state.DepositEnabled && deposit > 0);
    }

    private static string? ProblemText(int? amount, WGBankUiState state, int left)
    {
        if (amount is not { } value)
            return null;
        if (value > state.Balance)
            return Loc.GetString("wg-bank-error-funds");
        if (value > left)
            return Loc.GetString("wg-bank-error-limit", ("left", Format(left)));
        return null;
    }

    private void UpdateHistory()
    {
        if (_state is not { } state)
            return;

        var key = new StringBuilder();
        foreach (var entry in state.History)
        {
            key.Append(entry.Time.Ticks).Append(':').Append(entry.Amount).Append('|');
        }

        var built = key.ToString();
        if (built == _historyKey && _history.ChildCount > 0)
            return;

        _historyKey = built;
        _history.RemoveAllChildren();
        if (state.History.Count == 0)
        {
            _history.AddChild(new Label
            {
                Text = Loc.GetString("wg-bank-history-empty"),
                FontColorOverride = Dim,
                HorizontalAlignment = HAlignment.Center,
                Margin = new Thickness(0, 20, 0, 0),
            });
            return;
        }

        for (var i = 0; i < state.History.Count; i++)
        {
            _history.AddChild(HistoryRow(state.History[i], i % 2 == 0));
        }
    }

    private Control HistoryRow(WGBankEntry entry, bool shaded)
    {
        var incoming = entry.Kind is WGBankEntryKind.Deposit or WGBankEntryKind.TransferIn or WGBankEntryKind.AtmDeposit or WGBankEntryKind.Payment;
        var color = incoming ? Good : Bad;

        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
        row.AddChild(new PanelContainer
        {
            MinWidth = 3,
            Margin = new Thickness(0, 0, 8, 0),
            PanelOverride = new StyleBoxFlat { BackgroundColor = color },
        });

        var text = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, Margin = new Thickness(0, 3) };
        text.AddChild(new Label
        {
            Text = Loc.GetString($"wg-bank-history-{entry.Kind.ToString().ToLowerInvariant()}", ("party", entry.Party)),
            FontColorOverride = Bright,
            ClipText = true,
        });
        text.AddChild(new Label
        {
            Text = Loc.GetString("wg-bank-history-time", ("time", Clock(entry.Time))),
            FontOverride = _monoFont,
            FontColorOverride = Dim,
        });
        row.AddChild(text);

        row.AddChild(new Label
        {
            Text = Loc.GetString(incoming ? "wg-bank-history-in" : "wg-bank-history-out", ("amount", Format(entry.Amount))),
            FontColorOverride = color,
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(8, 0, 6, 0),
        });

        return Panel(row, shaded ? RowShade : Color.Transparent, null, default, default);
    }

    private BoxContainer Column()
    {
        return new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true };
    }

    private static BoxContainer Columns(Control left, Control right)
    {
        var row = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 12,
        };
        row.AddChild(left);
        row.AddChild(right);
        return row;
    }

    private Label Section(string key)
    {
        return new Label
        {
            Text = Loc.GetString(key),
            FontOverride = _smallFont,
            FontColorOverride = Gold,
            Margin = new Thickness(0, 0, 0, 3),
        };
    }

    private static Label Hint(string key)
    {
        return new Label { Text = Loc.GetString(key), FontColorOverride = Dim, Margin = new Thickness(0, 6, 0, 0) };
    }

    private static Label Problem(Label label)
    {
        label.FontColorOverride = Bad;
        label.Margin = new Thickness(0, 4, 0, 0);
        return label;
    }

    private static BoxContainer Row(string key, Label value)
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
        row.AddChild(new Label { Text = Loc.GetString(key), FontColorOverride = Dim, HorizontalExpand = true });
        value.FontColorOverride ??= Bright;
        value.HorizontalAlignment = HAlignment.Right;
        row.AddChild(value);
        return row;
    }

    private static Control Summary(params Control[] rows)
    {
        var column = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 3 };
        foreach (var row in rows)
        {
            column.AddChild(row);
        }

        return Panel(column, Field, null, default, new Thickness(8, 6));
    }

    private static StyleBoxFlat Box(Color background, Color? border, Thickness borderThickness, Thickness padding)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border ?? Color.Transparent,
            BorderThickness = border == null ? default : borderThickness,
            ContentMarginLeftOverride = padding.Left,
            ContentMarginTopOverride = padding.Top,
            ContentMarginRightOverride = padding.Right,
            ContentMarginBottomOverride = padding.Bottom,
        };
    }

    private static PanelContainer Panel(Control content, Color background, Color? border, Thickness borderThickness, Thickness padding, bool expand = false)
    {
        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = expand,
            PanelOverride = Box(background, border, borderThickness, padding),
        };
        panel.AddChild(content);
        return panel;
    }

    private static FormattedMessage Colored(string text, Color color)
    {
        var message = new FormattedMessage();
        message.PushColor(color);
        message.AddText(text);
        message.Pop();
        return message;
    }

    private static string Format(int amount)
    {
        return amount.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string Money(int amount)
    {
        return Loc.GetString("wg-bank-money", ("amount", Format(amount)));
    }

    /// <summary>A quick-amount chip's label: 1k, 250k, 1m.</summary>
    private static string Short(int amount)
    {
        return amount >= 1_000_000 && amount % 1_000_000 == 0
            ? $"{amount / 1_000_000}m"
            : amount >= 1_000 && amount % 1_000 == 0
                ? $"{amount / 1_000}k"
                : Format(amount);
    }

    private static string Clock(TimeSpan time)
    {
        return $"{(int) time.TotalHours}:{time.Minutes:D2}";
    }

    /// <summary>An amount to type or pick from quick chips, with a Max chip for all the account can send now.</summary>
    private sealed class AmountField : BoxContainer
    {
        private readonly WGBankPanel _owner;
        private readonly LineEdit _edit = new() { HorizontalExpand = true };

        public int? Amount => WGBankMath.ParseAmount(_edit.Text);

        public AmountField(WGBankPanel owner)
        {
            _owner = owner;
            Orientation = LayoutOrientation.Vertical;
            HorizontalExpand = true;

            var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
            _edit.PlaceHolder = Loc.GetString("wg-bank-amount-placeholder");
            _edit.OnTextChanged += _ => Changed();
            row.AddChild(_edit);
            row.AddChild(new Label
            {
                Text = Loc.GetString("wg-bank-currency"),
                FontColorOverride = Dim,
                VerticalAlignment = VAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
            });
            AddChild(row);

            var chips = new GridContainer { Columns = 4, HorizontalExpand = true, Margin = new Thickness(0, 4, 0, 0) };
            foreach (var quick in Quick)
            {
                var value = quick;
                chips.AddChild(Chip(Short(value), () => Set(value)));
            }

            chips.AddChild(Chip(Loc.GetString("wg-bank-amount-max"), () =>
            {
                if (_owner._state is { } state)
                    Set(WGBankMath.Allowed(state.Balance, state.Limit, state.Withdrawn));
            }));
            AddChild(chips);
        }

        public void Set(int amount)
        {
            _edit.Text = amount > 0 ? amount.ToString(CultureInfo.InvariantCulture) : string.Empty;
            Changed();
        }

        private void Changed()
        {
            _owner._withdraw.Disarm();
            _owner._transfer.Disarm();
            _owner.Refresh();
        }

        private static Button Chip(string text, Action pressed)
        {
            var button = new Button { Text = text, HorizontalExpand = true, Margin = new Thickness(0, 0, 2, 2) };
            button.StyleClasses.Add("OpenBoth");
            button.OnPressed += _ => pressed();
            return button;
        }
    }

    /// <summary>A button that asks once more before it acts, so a slip of the mouse moves no money.</summary>
    private sealed class ConfirmButton : Button
    {
        private const string Armed = "Caution";
        private const string Ready = "ButtonColorGreen";

        private bool _armed;
        private string _label = string.Empty;

        public event Action? Confirmed;

        public ConfirmButton()
        {
            MinHeight = 32;
            HorizontalExpand = true;
            ClipText = true;
            StyleClasses.Add(Ready);
            OnPressed += _ => Press();
        }

        public void Set(string label, bool enabled)
        {
            if (label != _label || !enabled)
                _armed = false;

            _label = label;
            Disabled = !enabled;
            Show();
        }

        public void Disarm()
        {
            _armed = false;
            Show();
        }

        private void Press()
        {
            if (!_armed)
            {
                _armed = true;
                Show();
                return;
            }

            _armed = false;
            Show();
            Confirmed?.Invoke();
        }

        private void Show()
        {
            Text = _armed ? Loc.GetString("wg-bank-confirm", ("action", _label)) : _label;
            StyleClasses.Remove(_armed ? Ready : Armed);
            if (!StyleClasses.Contains(_armed ? Armed : Ready))
                StyleClasses.Add(_armed ? Armed : Ready);
        }
    }
}
