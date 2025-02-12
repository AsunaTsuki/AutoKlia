using ECommons;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.EzIpcManager;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoKlia.Helpers
{
    public unsafe class Location
    {

        private readonly Plugin plugin;
        public string CurrentLocation;

        TaskManager TaskManager;

        [EzIPC("Lifestream.GetRealTerritoryType", false)] public Func<uint> GetRealTerritoryType;

        public Location(Plugin plugin)
        {
            this.plugin = plugin;
            EzIPC.Init(this);
            CurrentLocation = UpdateLocation();
            TaskManager = new();
            Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
        }

        private void OnTerritoryChanged(ushort newTerritory)
        {
            TaskManager.Abort();
            TaskManager.EnqueueDelay(1, true);
            TaskManager.Enqueue(() =>
            {
                if (Svc.ClientState.LocalPlayer != null)
                {
                    //do your stuff
                    CurrentLocation = UpdateLocation();
                    return true;
                }
                return false;
            });
            
        }

        public unsafe string UpdateLocation()
        {
            if (Svc.ClientState?.LocalPlayer != null)
            {
                var h = HousingManager.Instance();
                var world = Svc.ClientState?.LocalPlayer?.CurrentWorld.Value.Name;
                var ward = h->GetCurrentWard() + 1; //0 if not in ward
                var plot = h->GetCurrentPlot() + 1; //0 if not in plot boundaries
                var apartment = h->GetCurrentRoom(); //0 if in lobby

                var residential = Svc.Data.GetExcelSheet<TerritoryType>().FirstOrNull(x => x.TerritoryIntendedUse.RowId == (uint)TerritoryIntendedUseEnum.Residential_Area && x.PlaceNameRegion.RowId == Svc.Data.GetExcelSheet<TerritoryType>().GetRow(GetRealTerritoryType()).PlaceNameRegion.RowId)?.PlaceName.Value.Name;

                string result = "";

                if (ward == 0)
                {
                    result = "Not in a housing area";
                }
                else if (apartment > 0)
                {
                    result = $"{world} {residential} Ward: {ward} Room: {apartment}";
                }
                else if (plot != 0)
                {
                    result = $"{world} {residential} Ward: {ward} Plot: {plot}";
                }
                else
                {
                    result = $"{world} {residential} Ward: {ward}";
                }


                return result;
            }
            else
            { return "Not Logged In"; }
        }
    }

}
