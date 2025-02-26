using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using ECommons;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using static FFXIVClientStructs.ThisAssembly;
using ECommons.ImGuiMethods;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Text;
using System.Drawing;
using System.Linq;

namespace AutoKlia.Windows
{
    // Classes to deserialize the JSON response from your Flask endpoint.
    public class Payout
    {
        public int player_id { get; set; }
        public string name { get; set; }
        public double amount_owed { get; set; }
        public int manne_set { get; set; }
        public string lifestream { get; set; }
    }

    public class Tab
    {
        public int player_id { get; set; }
        public string name { get; set; }
        public double amount_owed { get; set; }
        public int credit_score { get; set; }
        public string overdue {  get; set; }
    }

    public class TabsResponse
    {
        public string status { get; set; }
        public string message { get; set; }
        public List<Tab> tabs { get; set; }
    }


    public class PayoutResponse
    {
        public string status { get; set; }
        public string message { get; set; }
        public List<Payout> payouts { get; set; }
    }

    public class TabPaymentResponse
    {
        public string status { get; set; }
        public string message { get; set; }
    }

    public class MainWindow : Window, IDisposable
    {
        private Plugin Plugin;
        
        private List<Payout> _payouts = new();
        private List<Tab> _tabs = new();

        private bool _isLoading = false;
        private string _errorMessage = "";
        private readonly HttpClient _httpClient;
        

        private readonly Dictionary<int, int> _payAmountInputs = new();
        private readonly Dictionary<int, bool> _takeRemainderFlags = new();

        // Variables used for the confirmation modal popup.
        private bool _showConfirmDialog = false;
        private int _confirmPlayerId;
        private double _confirmPayAmount;
        private bool _confirmTakeRemainder;
        private string _confirmPlayerName = "";
        private double _confirmBalance;

        private bool _showTabConfirmDialog = false;
        private int _confirmTabPlayerId;
        private double _confirmTabPayAmount;
        private string _confirmTabPlayerName = "";


        private BalancesResponse? _balancesResponse;
        private readonly Helpers.Location locationHelper;
        

        public MainWindow(Plugin plugin)
            : base("AutoKlia##AutoKliaWindow")//, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(1200, 400),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };

            Plugin = plugin;
            
            _httpClient = new HttpClient();  // Instantiate the HttpClient here.
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Plugin.Configuration.apikey);
            locationHelper = new Helpers.Location(Plugin);

        }

        public void Dispose()
        {
            // Dispose of any resources if necessary.
            _httpClient.Dispose();
        }

        public override void Draw()
        {
            // Require holding CTRL to enable the payout button.
            bool isCtrlPressed = ImGui.GetIO().KeyCtrl;

            ImGuiEx.RightFloat(() =>
            {
                // Optionally show current location.
                ImGui.TextUnformatted($"Current Address: {locationHelper.CurrentLocation}");
                ImGui.SameLine();
                // Refresh payouts button.
                if (ImGui.Button("Refresh Data"))
                {
                    _ = Task.Run(async () =>
                    {
                        await RefreshPayoutsAsync();
                        await RefreshBalancesAsync();
                        await RefreshTabsAsync();
                    });
                }
            });


            // Begin a tab bar that holds our two tabs.
            if (ImGui.BeginTabBar("MainTabBar"))
            {
                // ----- PAYOUTS TAB -----
                if (ImGui.BeginTabItem("Payouts"))
                {

                    
                    bool showManneOnly = Plugin.Configuration.ShowMannequinReadyOnly;
                    if (ImGui.Checkbox($"Limit to set Manne", ref showManneOnly))
                    {
                        Plugin.Configuration.ShowMannequinReadyOnly = showManneOnly;
                    }

                    if (_isLoading)
                        ImGui.TextUnformatted("Loading data...");

                    if (!string.IsNullOrEmpty(_errorMessage))
                        ImGui.TextColored(new Vector4(1, 0, 0, 1), _errorMessage);

                    ImGui.Spacing();

                    if (_payouts.Count == 0)
                    {
                        ImGui.TextUnformatted("No payouts available.");
                    }
                    else
                    {
                        // Begin a table with 6 columns for the payouts.
                        if (ImGui.BeginTable("PayoutTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                        {
                            // Column headers.
                            ImGui.TableSetupColumn("Name");
                            ImGui.TableSetupColumn("Amount Owed", ImGuiTableColumnFlags.WidthFixed, 180f);
                            ImGui.TableSetupColumn("Pay Amount | Tip Rest", ImGuiTableColumnFlags.WidthFixed, 180f);
                            ImGui.TableSetupColumn("Manne Set", ImGuiTableColumnFlags.WidthFixed, 90f);
                            ImGui.TableSetupColumn("Lifestream", ImGuiTableColumnFlags.WidthFixed, 300f);
                            ImGui.TableSetupColumn("Teleport");
                            ImGui.TableHeadersRow();

                            foreach (var payout in _payouts)
                            {
                                // Optionally filter payouts.
                                if (Plugin.Configuration.ShowMannequinReadyOnly && payout.manne_set == 0)
                                    continue;

                                ImGui.TableNextRow();

                                // Color the row if the payout is “ready.”
                                if (payout.manne_set == 1)
                                {
                                    uint greenColor = ImGui.GetColorU32(new Vector4(0.0f, 1.0f, 0.0f, 0.3f));
                                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, greenColor);
                                }

                                // Name
                                ImGui.TableNextColumn();
                                ImGuiEx.TextV(payout.name);

                                // Amount Owed
                                ImGui.TableNextColumn();
                                ImGui.TextUnformatted($"{payout.amount_owed:N0} gil");

                                // Pay Amount input text and checkbox for "take remainder"
                                ImGui.TableNextColumn();
                                if (!_payAmountInputs.ContainsKey(payout.player_id))
                                {
                                    _payAmountInputs[payout.player_id] = (int)payout.amount_owed;
                                }
                                var payAmountInt = _payAmountInputs[payout.player_id];
                                if (ImGuiEx.InputFancyNumeric($"###payamount-{payout.player_id}", ref payAmountInt, 0))
                                {
                                    _payAmountInputs[payout.player_id] = payAmountInt;
                                }
                                ImGui.SameLine();
                                if (!_takeRemainderFlags.ContainsKey(payout.player_id))
                                    _takeRemainderFlags[payout.player_id] = false;
                                bool takeRemainder = _takeRemainderFlags[payout.player_id];
                                if (ImGui.Checkbox($"###takeRest-{payout.player_id}", ref takeRemainder))
                                {
                                    _takeRemainderFlags[payout.player_id] = takeRemainder;
                                }

                                // Manne Set
                                ImGui.TableNextColumn();
                                ImGui.TextUnformatted(payout.manne_set == 1 ? "Yes" : "No");

                                // Lifestream
                                ImGui.TableNextColumn();
                                ImGui.TextUnformatted(payout.lifestream);

                                // Teleport & Payout buttons.
                                ImGui.TableNextColumn();
                                if (ImGui.Button("Teleport##" + payout.player_id))
                                {
                                    if (!string.IsNullOrEmpty(payout.lifestream) && payout.lifestream.StartsWith("/li"))
                                    {
                                        string cleanedLifestream = Regex.Replace(payout.lifestream, @"\s*\(.*?\)", "");
                                        Svc.Commands.ProcessCommand(cleanedLifestream);
                                    }
                                    else
                                    {
                                        Svc.Chat.Print("No valid lifestream command found");
                                    }
                                }
                                ImGui.SameLine();
                                
                                if (!isCtrlPressed) { ImGui.BeginDisabled(); }
                                if (ImGui.Button("Payout (CTRL)##" + payout.player_id))
                                {
                                    if (_payAmountInputs.TryGetValue(payout.player_id, out int payAmount))
                                    {
                                        bool takeRemainderFlag = _takeRemainderFlags.TryGetValue(payout.player_id, out var flag) && flag;
                                        _confirmPlayerId = payout.player_id;
                                        _confirmPayAmount = payAmount;
                                        _confirmTakeRemainder = takeRemainderFlag;
                                        _confirmPlayerName = payout.name;
                                        _confirmBalance = payout.amount_owed;
                                        _showConfirmDialog = true;
                                    }
                                    else
                                    {
                                        _errorMessage = $"Invalid pay amount for player {payout.name}.";
                                    }
                                }
                                if (!isCtrlPressed) { ImGui.EndDisabled(); }
                            }
                            ImGui.EndTable();
                        }

                        // --- Total Line Calculation ---
                        double totalOwed = 0;
                        foreach (var payout in _payouts)
                        {
                            if (Plugin.Configuration.ShowMannequinReadyOnly && payout.manne_set == 0)
                                continue;
                            totalOwed += payout.amount_owed;
                        }
                        ImGui.Spacing();

                        // Show current gil.
                        int currentGil = 0;
                        unsafe { currentGil = InventoryManager.Instance()->GetInventoryItemCount(1); }
                        ImGui.TextUnformatted($"Current Gil:     {currentGil:N0} gil");
                        ImGui.TextUnformatted("Total Owed: ");
                        ImGui.SameLine();

                        // Color the total based on whether the current gil covers the total owed.
                        if (currentGil >= totalOwed)
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0f, 1f, 0f, 1f));
                        else
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0f, 0f, 1f));
                        ImGui.TextUnformatted($"{totalOwed:N0} gil");
                        ImGui.PopStyleColor();

                        
                    }
                    ImGui.EndTabItem();
                }

                // ----- TABS TAB -----
                if (ImGui.BeginTabItem("Tabs"))
                {
                    if (_isLoading)
                        ImGui.TextUnformatted("Loading data...");
                    if (!string.IsNullOrEmpty(_errorMessage))
                        ImGui.TextColored(new Vector4(1, 0, 0, 1), _errorMessage);

                    ImGui.Spacing();

                    if (_tabs == null || _tabs.Count == 0)
                    {
                        ImGui.TextUnformatted("No tabs data available.");
                    }
                    else
                    {
                        // Begin a table with 3 columns: Player, Amount Owed, and Actions.
                        if (ImGui.BeginTable("TabsTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                        {
                            ImGui.TableSetupColumn("Player");
                            ImGui.TableSetupColumn("Credit Score", ImGuiTableColumnFlags.WidthFixed, 100f);
                            ImGui.TableSetupColumn("Overdue");
                            ImGui.TableSetupColumn("Amount Owed", ImGuiTableColumnFlags.WidthFixed, 180f);
                            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 120f);
                            ImGui.TableHeadersRow();

                            foreach (var tab in _tabs)
                            {
                                ImGui.TableNextRow();

                                // Player Name
                                ImGui.TableNextColumn();
                                ImGuiEx.TextV(tab.name);

                                // Credit Score
                                ImGui.TableNextColumn();
                                ImGuiEx.TextV($"{tab.credit_score}");


                                // Overdue
                                ImGui.TableNextColumn();
                                ImGuiEx.TextV($"{tab.overdue}");

                                // Amount Owed
                                ImGui.TableNextColumn();
                                ImGuiEx.TextV($"{tab.amount_owed:N0} gil");

                                // Actions (e.g. Teleport)
                                ImGui.TableNextColumn();


                                if (!isCtrlPressed) { ImGui.BeginDisabled(); }

                                if (ImGui.Button("Tab Paid##Tabs" + tab.player_id))
                                {
                                    _confirmTabPlayerId = tab.player_id;
                                    _confirmTabPayAmount = tab.amount_owed;
                                    _confirmTabPlayerName = tab.name;
                                    _showTabConfirmDialog = true;

                                }
                                if (!isCtrlPressed) { ImGui.EndDisabled(); }
                            }
                            ImGui.EndTable();
                        }

                        // --- Total Line Calculation ---
                        double totalOwed = 0;
                        foreach (var tab in _tabs)
                        {
                            totalOwed += tab.amount_owed;
                        }
                        ImGui.Spacing();

                        ImGui.TextUnformatted("Total Owed: ");
                        ImGui.SameLine();
                        ImGui.TextUnformatted($"{totalOwed:N0} gil");

                    }
                    ImGui.EndTabItem();
                }

                // ----- BALANCES TAB -----
                if (ImGui.BeginTabItem("Balances"))
                {

                    if (_isLoading)
                        ImGui.TextUnformatted("Loading data...");

                    if (!string.IsNullOrEmpty(_errorMessage))
                        ImGui.TextColored(new Vector4(1, 0, 0, 1), _errorMessage);

                    
                    ImGui.Spacing();

                    // Check if balances data is available.
                    if (_balancesResponse == null || _balancesResponse.balances == null)
                    {
                        ImGui.TextUnformatted("No balance data available.");
                    }
                    else
                    {
                        // --- Table for People Who Owe You Money (Owed Data) ---
                        ImGui.TextUnformatted("Players Who Owe Me Money:");
                        if (ImGui.BeginTable("OwesTable", 7, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                        {
                            ImGui.TableSetupColumn("Player");
                            ImGui.TableSetupColumn("Status");
                            ImGui.TableSetupColumn("Credit Score", ImGuiTableColumnFlags.WidthFixed, 120f);
                            ImGui.TableSetupColumn("Last Pay");
                            ImGui.TableSetupColumn("Manne", ImGuiTableColumnFlags.WidthFixed, 90f);
                            ImGui.TableSetupColumn("Overdue");
                            ImGui.TableSetupColumn("Lifestream", ImGuiTableColumnFlags.WidthFixed, 300f);
                            ImGui.TableHeadersRow();

                            foreach (var player in _balancesResponse.balances.owes.players)
                            {
                                ImGui.TableNextRow();
                                ImGui.TableNextColumn();
                                ImGuiEx.TextV(player.player);
                                ImGui.TableNextColumn();
                                ImGuiEx.TextV(player.status);
                                ImGui.TableNextColumn();
                                ImGuiEx.TextV(player.credit_score.ToString());
                                ImGui.TableNextColumn();
                                ImGuiEx.TextV(player.last_pay);
                                ImGui.TableNextColumn();
                                ImGuiEx.TextV(player.manne);
                                ImGui.TableNextColumn();
                                ImGuiEx.TextV(player.overdue);
                                ImGui.TableNextColumn();
                                ImGuiEx.TextV(player.lifestream);
                                
                            }
                            ImGui.EndTable();
                        }
                        ImGuiEx.TextV($"Total Owed to me: {_balancesResponse.balances.owes.total_owing:N0} gil");
                        

                        ImGui.Spacing();
                        ImGui.Spacing();

                        // --- Table for People You Owe Money (Owes Data) ---
                        ImGui.TextUnformatted("Players I Owe Money:");
                        if (ImGui.BeginTable("OwedTable", 7, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                        {
                            ImGui.TableSetupColumn("Player");
                            ImGui.TableSetupColumn("Status");
                            ImGui.TableSetupColumn("Credit Score", ImGuiTableColumnFlags.WidthFixed, 120f);
                            ImGui.TableSetupColumn("Last Pay");
                            ImGui.TableSetupColumn("Manne", ImGuiTableColumnFlags.WidthFixed, 90f);
                            ImGui.TableSetupColumn("Overdue");
                            ImGui.TableSetupColumn("Lifestream", ImGuiTableColumnFlags.WidthFixed, 300f);
                            ImGui.TableHeadersRow();

                            foreach (var player in _balancesResponse.balances.owed.players)
                            {
                                ImGui.TableNextRow();
                                ImGui.TableNextColumn();
                                ImGuiEx.TextV(player.player);
                                ImGui.TableNextColumn();
                                ImGuiEx.TextV(player.status);
                                ImGui.TableNextColumn();
                                ImGuiEx.TextV(player.credit_score.ToString());
                                ImGui.TableNextColumn();
                                ImGuiEx.TextV(player.last_pay);
                                ImGui.TableNextColumn();
                                ImGuiEx.TextV(player.manne);
                                ImGui.TableNextColumn();
                                ImGuiEx.TextV(player.overdue);
                                ImGui.TableNextColumn();
                                ImGuiEx.TextV(player.lifestream);
                            }
                            ImGui.EndTable();
                        }
                        ImGuiEx.TextV($"Total Owed to players: {_balancesResponse.balances.owed.total_owed:N0} gil");
                    }
                    ImGui.EndTabItem();
                }




                ImGui.EndTabBar();
            }

            // Draw the confirmation modal popup if needed.
            if (_showConfirmDialog)
            {
                var size = ImGui.GetWindowSize();
                ImGuiHelpers.ForceNextWindowMainViewport();
                ImGuiHelpers.SetNextWindowPosRelativeMainViewport(ImGuiHelpers.MainViewport.Size / 2 - size / 2);
                ImGui.OpenPopup("Confirm Payment");
            }
            if (ImGui.BeginPopupModal("Confirm Payment", ref _showConfirmDialog, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextWrapped(
                    $"Please confirm the following payout:\n\n" +
                    $"Player: {_confirmPlayerName}\n" +
                    $"Balance: {_confirmBalance:N0} gil\n" +
                    $"Pay Amount: {_confirmPayAmount:N0} gil\n" +
                    $"Take Remainder: {(_confirmTakeRemainder ? "Yes" : "No")}");
                ImGui.Spacing();

                if (ImGui.Button("Confirm", new Vector2(120, 0)))
                {
                    _ = MarkAsPaidAsync(_confirmPlayerId, _confirmPayAmount, _confirmTakeRemainder);
                    ImGui.CloseCurrentPopup();

                    string message = $"Successfully paid out {_confirmPlayerName} {_confirmPayAmount:N0} gil";
                    if (_confirmTakeRemainder)
                        message += " and took the remainder as a tip";
                    Svc.Chat.Print(message);

                    _ = Task.Run(async () =>
                    {
                        await RefreshPayoutsAsync();
                        await RefreshTabsAsync();
                        await RefreshBalancesAsync();
                    });

                    _showConfirmDialog = false;
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(120, 0)))
                {
                    ImGui.CloseCurrentPopup();
                    _showConfirmDialog = false;
                }
                ImGui.EndPopup();
            }



            if (_showTabConfirmDialog)
            {
                var size = ImGui.GetWindowSize();
                ImGuiHelpers.ForceNextWindowMainViewport();
                ImGuiHelpers.SetNextWindowPosRelativeMainViewport(ImGuiHelpers.MainViewport.Size / 2 - size / 2);
                ImGui.OpenPopup("Confirm Tab Payment");
            }
            if (ImGui.BeginPopupModal("Confirm Tab Payment", ref _showTabConfirmDialog, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextWrapped(
                    $"Please confirm the following tab payment:\n\n" +
                    $"Player: {_confirmTabPlayerName}\n" +
                    $"Pay Amount: {_confirmTabPayAmount:N0} gil\n");
                ImGui.Spacing();

                if (ImGui.Button("Confirm", new Vector2(120, 0)))
                {
                    _ = SendTabPaymentAsync(_confirmTabPlayerName, _confirmTabPlayerId, (int)_confirmTabPayAmount, Plugin.adminNameWorld);
                    ImGui.CloseCurrentPopup();

                    string message = $"Confirmed tab payment for {_confirmTabPlayerName} {_confirmTabPayAmount:N0} gil";
                    
                    Svc.Chat.Print(message);

                    _ = Task.Run(async () =>
                    {
                        await RefreshPayoutsAsync();
                        await RefreshBalancesAsync();
                        await RefreshTabsAsync();
                    });

                    _showTabConfirmDialog = false;
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(120, 0)))
                {
                    ImGui.CloseCurrentPopup();
                    _showTabConfirmDialog = false;
                }
                ImGui.EndPopup();
            }
        }


        public class BalancesResponse
        {
            public Balances balances { get; set; }
            public string status { get; set; }
            public DateTime timestamp { get; set; }
        }

        public class Balances
        {
            public OwedData owed { get; set; }
            public OwesData owes { get; set; }
        }

        public class OwedData
        {
            public List<BalancePlayer> players { get; set; }
            public long total_owed { get; set; }
        }

        public class OwesData
        {
            public List<BalancePlayer> players { get; set; }
            public long total_owing { get; set; }
        }

        public class BalancePlayer
        {
            public int credit_score { get; set; }
            public string last_pay { get; set; }
            public string lifestream { get; set; }
            public string manne { get; set; }
            public string overdue { get; set; }
            public string player { get; set; }
            public string status { get; set; }
        }


        /// <summary>
        /// Asynchronously fetches payout data from the Flask endpoint.
        /// </summary>
        public async Task RefreshPayoutsAsync()
        {
            _isLoading = true;
            _errorMessage = "";
            try
            {
                // Replace with the URL of your Flask endpoint.
                var url = $"{Plugin.EndpointURL}/api/payouts";
                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var payoutResponse = JsonSerializer.Deserialize<PayoutResponse>(json);
                    if (payoutResponse != null && payoutResponse.payouts != null)
                        //_payouts = payoutResponse.payouts;
                        // Sort payouts by the 'name' property.
                        _payouts = payoutResponse.payouts
                                    .OrderBy(p => p.name)
                                    .ToList();
                    else
                        _errorMessage = "Failed to parse payout data.";
                }
                else
                {
                    _errorMessage = $"HTTP error: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                _errorMessage = "Error: " + ex.Message;
            }
            finally
            {
                _isLoading = false;
            }
        }



        public async Task RefreshTabsAsync()
        {
            try
            {
                var url = $"{Plugin.EndpointURL}/api/tabs";
                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var tabsResponse = JsonSerializer.Deserialize<TabsResponse>(json);
                    if (tabsResponse != null && tabsResponse.tabs != null)
                    {
                        //_tabs = tabsResponse.tabs;
                        _tabs = tabsResponse.tabs
                            .OrderBy(t => t.name)
                            .ToList();
                    }
                    else
                    {
                        _errorMessage = "Failed to parse tabs data.";
                    }
                }
                else
                {
                    _errorMessage = $"HTTP error retrieving tabs: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                _errorMessage = "Error retrieving tabs: " + ex.Message;
            }
        }


        public async Task RefreshBalancesAsync()
        {
            // Call GetBalancesAsync() to retrieve the balances data from your API
            _balancesResponse = await GetBalancesAsync();
        }

        /// <summary>
        /// Asynchronously retrieves balance data from the /api/balances endpoint.
        /// </summary>
        /// <returns>A <see cref="BalancesResponse"/> object containing the balances data.</returns>
        public async Task<BalancesResponse> GetBalancesAsync()
        {
            try
            {
                var url = $"{Plugin.EndpointURL}/api/balances";
                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var balancesResponse = JsonSerializer.Deserialize<BalancesResponse>(json);
                    if (balancesResponse != null && balancesResponse.balances != null)
                    {
                        // Sort the list of players who are owed money
                        if (balancesResponse.balances.owed != null && balancesResponse.balances.owed.players != null)
                        {
                            balancesResponse.balances.owed.players = balancesResponse.balances.owed.players
                                .OrderBy(p => p.player)
                                .ToList();
                        }

                        // Sort the list of players who owe money
                        if (balancesResponse.balances.owes != null && balancesResponse.balances.owes.players != null)
                        {
                            balancesResponse.balances.owes.players = balancesResponse.balances.owes.players
                                .OrderBy(p => p.player)
                                .ToList();
                        }
                    }


                    return balancesResponse;
                }
                else
                {
                    // Return an error-like response if HTTP request fails
                    return new BalancesResponse
                    {
                        status = "Error",
                        balances = null,
                        timestamp = DateTime.Now
                    };
                }
            }
            catch (Exception ex)
            {
                // Return an error-like response if an exception occurs
                return new BalancesResponse
                {
                    status = "Error",
                    balances = null,
                    timestamp = DateTime.Now
                };
            }
        }
        /// <summary>
        /// Sends a tab payment request to the Flask API.
        /// </summary>
        /// <param name="playerName">The player's name.</param>
        /// <param name="playerId">The player's ID.</param>
        /// <param name="amount">The payment amount (must be greater than 0).</param>
        /// <param name="notes">Optional notes for the payment.</param>
        /// <param name="dateStr">Optional date string in MM-DD-YYYY format.</param>
        /// <returns>A TabPaymentResponse indicating the result.</returns>
        public async Task<TabPaymentResponse> SendTabPaymentAsync(
            string playerName,
            int playerId,
            int amount,
            string adminName,
            string notes = null,
            string dateStr = null
            )
        {
            // Build the JSON payload.
            var payload = new
            {
                player_name = playerName,
                player_id = playerId,
                amount = amount,
                notes = notes,
                date_str = dateStr,
                adminNameWorld = Plugin.adminNameWorld,
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // Construct the URL. Adjust the scheme (https/http) as needed.
            var url = $"{Plugin.EndpointURL}/api/tabpayment";

            try
            {
                var response = await _httpClient.PostAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    var tabPaymentResponse = JsonSerializer.Deserialize<TabPaymentResponse>(jsonResponse);
                    return tabPaymentResponse;
                }
                else
                {
                    // Return an error response if the HTTP status code is not successful.
                    return new TabPaymentResponse
                    {
                        status = "error",
                        message = $"HTTP error: {response.StatusCode}"
                    };
                }
            }
            catch (Exception ex)
            {
                // Return an error response if an exception occurs.
                return new TabPaymentResponse
                {
                    status = "error",
                    message = "Exception: " + ex.Message
                };
            }
        }


    private async Task MarkAsPaidAsync(int playerId, double payAmount, bool takeRemainder)
        {
            try
            {
                // Create the payload.
                var payload = new
                {
                    player_id = playerId,
                    // You might send the payout amount (which your server can use to update the balance)
                    amount = payAmount,
                    // Optionally, include any other fields (e.g. a timestamp or operator ID)
                    take_remainder = takeRemainder,
                    adminNameWorld = Plugin.adminNameWorld,
                };

                var jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

                // Replace with the URL of your Flask endpoint that handles marking a payout as paid.
                var url = $"{Plugin.EndpointURL}/api/mark_paid";
                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    // Optionally, refresh your data or notify the user.
                    // For example, you might re-fetch the payout list:
                    _ = RefreshPayoutsAsync();
                }
                else
                {
                    _errorMessage = $"Payout failed: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                _errorMessage = "Error in MarkAsPaidAsync: " + ex.Message;
            }
        }

    }
}
