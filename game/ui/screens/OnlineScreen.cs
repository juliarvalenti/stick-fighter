using Godot;

namespace StickFighter.UI.Screens;

// Online play, direct connect (no server yet): host a match and give the other player your address,
// or join one by typing theirs. Each page is a panel built from the kit; whoever shows it (Main)
// does the networking, on Chosen, and puts what's happening in Status.
//   Choose: Host / Join / Back
//   Host:   your address, the status (waiting, or why it failed), Cancel
//   Join:   the host's address (IP:port), Connect, Back, and the status
public partial class OnlineScreen : Screen
{
    public enum Page
    {
        Choose,
        Host,
        Join,
    }

    public const string HostItem = "Host";
    public const string JoinItem = "Join";
    public const string BackItem = "Back";
    public const string CancelItem = "Cancel";
    public const string ConnectItem = "Connect";

    private readonly Page _page;
    private readonly string _address;
    private Label? _status;
    private LineEdit? _field;

    // `address`: on Host, yours to read out; on Join, the last one typed, to start from.
    public OnlineScreen(Page page = Page.Choose, string address = "", string status = "")
    {
        _page = page;
        _address = address;
        Status = status;
    }

    // What's happening: "Waiting for a player…", "Connecting…", or why it failed.
    public string Status
    {
        get => _status?.Text ?? _initialStatus;
        set
        {
            _initialStatus = value;
            if (_status is not null)
            {
                _status.Text = value;
                _status.Visible = value.Length > 0;
            }
        }
    }

    private string _initialStatus = "";

    // The address typed on the Join page.
    public string Address => _field?.Text.Trim() ?? _address;

    public Page Showing => _page;

    public override void _Ready()
    {
        var panel = Ui.Panel();
        panel.CustomMinimumSize = new Vector2(460, 0);
        var column = new VBoxContainer();
        panel.AddChild(column);
        column.AddChild(new Label { Text = "ONLINE", ThemeTypeVariation = UiTheme.Heading, HorizontalAlignment = HorizontalAlignment.Center });

        Button first;
        switch (_page)
        {
            case Page.Host:
                column.AddChild(Ui.Label("Give the other player your address:", UiTheme.Muted));
                column.AddChild(new Label { Text = _address, ThemeTypeVariation = UiTheme.Heading, HorizontalAlignment = HorizontalAlignment.Center });
                AddStatus(column);
                first = Menu(column, CancelItem);
                break;
            case Page.Join:
                column.AddChild(Ui.Label("The host's address (IP:port):", UiTheme.Muted));
                _field = new LineEdit
                {
                    Text = _address,
                    PlaceholderText = "192.168.1.20:7000",
                    CustomMinimumSize = new Vector2(400, 52),
                    Alignment = HorizontalAlignment.Center,
                };
                _field.TextSubmitted += _ => Choose(ConnectItem);
                column.AddChild(_field);
                AddStatus(column);
                first = Menu(column, ConnectItem, BackItem);
                break;
            default:
                first = Menu(column, HostItem, JoinItem, BackItem);
                AddStatus(column);
                break;
        }

        AddChild(panel);
        CenterAt(panel, 160);
        if (_field is not null)
            _field.GrabFocus();
        else
            first.GrabFocus();
    }

    private void AddStatus(VBoxContainer column)
    {
        _status = new Label
        {
            Text = _initialStatus,
            Visible = _initialStatus.Length > 0, // no blank line when there's nothing to say
            ThemeTypeVariation = UiTheme.Strong,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(400, 0),
        };
        column.AddChild(_status);
    }
}
