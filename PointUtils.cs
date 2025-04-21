using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Life;
using Life.CheckpointSystem;
using Life.DB;
using Life.Network;
using Life.UI;
using Life.VehicleSystem;
using Mirror;
using Socket.Newtonsoft.Json;
using UnityEngine;

namespace PointUtils
{
    public class PointUtils : Plugin
    {
        private static readonly string filePath = "pointData.json";
        private static List<PointData> points = new List<PointData>();

        public PointUtils(IGameAPI api) : base(api) { }

        public override void OnPluginInit()
        {
            base.OnPluginInit();
            Debug.Log("\u001b[32m[By Spicy...ice] Point Go Fast ON\u001b[0m");

            new SChatCommand("/point", "", "/point", (player, args) => { PointPanel(player); }).Register();
        }

        public override void OnPlayerSpawnCharacter(Player player, NetworkConnection conn, Characters character)
        {
            base.OnPlayerSpawnCharacter(player, conn, character);

            LoadPoints(player);
        }

        private void PointPanel(Player player)
        {
            if (player.account.adminLevel < 5) { return; }

            UIPanel pntPanel = new UIPanel("pntPanel", UIPanel.PanelType.Tab);
            pntPanel.SetTitle("<color=#156884>Gestion Point(s)</color>");
            pntPanel.AddTabLine("Créer un point bleu (garage)", _ =>
            {
                CreatePlayerGaragePoint(player);
                player.ClosePanel(pntPanel);
            });
            pntPanel.AddTabLine("Créer un point orange (garage)", _ =>
            {
                CreateVehicleGaragePoint(player);
                player.ClosePanel(pntPanel);
            });
            pntPanel.AddButton("<color=#3E9F18>Sélectionner</color>", _ => pntPanel.SelectTab());
            pntPanel.AddButton("<color=#B63637>Fermer</color>", _ => player.ClosePanel(pntPanel));
            player.ShowPanelUI(pntPanel);
        }

        private void CreatePlayerGaragePoint(Player player)
        {
            Vector3 playerPosition = player.setup.transform.position;

            UIPanel vhSpawnPanel = new UIPanel("vhSpawnPanel", UIPanel.PanelType.Text);
            vhSpawnPanel.SetTitle("<color=#156884>Spawn véhicule</color>");
            vhSpawnPanel.SetText("Va à l'endroit où tu veux que le véhicule Spawn");
            vhSpawnPanel.AddButton("<color=#3E9F18>Accepter</color>", _ =>
            {
                Vector3 spawnPosition = player.setup.transform.position;
                PointData newPoint = new PointData
                {
                    Type = "Spawn",
                    X = playerPosition.x,
                    Y = playerPosition.y,
                    Z = playerPosition.z,
                    SpawnX = spawnPosition.x,
                    SpawnY = spawnPosition.y,
                    SpawnZ = spawnPosition.z,
                    PlayerId = player.character.Id
                };
                points.Add(newPoint);
                SavePoints();

                NCheckpoint blueGaragePoint = new NCheckpoint(player.netId, playerPosition, ui => garagePanel(player));
                player.CreateCheckpoint(blueGaragePoint);
                player.ClosePanel(vhSpawnPanel);
            });
            vhSpawnPanel.AddButton("<color=#B63637>Annuler</color>", _ => player.ClosePanel(vhSpawnPanel));
            player.ShowPanelUI(vhSpawnPanel);
        }

        private void CreateVehicleGaragePoint(Player player)
        {
            Vector3 playerPosition = player.setup.transform.position;
            PointData newPoint = new PointData
            {
                Type = "Stow",
                X = playerPosition.x,
                Y = playerPosition.y,
                Z = playerPosition.z,
                PlayerId = player.character.Id
            };
            points.Add(newPoint);
            SavePoints();

            float positionX = player.setup.transform.position.x;
            float positionY = player.setup.transform.position.y;
            float positionZ = player.setup.transform.position.z;

            NVehicleCheckpoint orangeGaragePoint = new NVehicleCheckpoint(player.netId, new Vector3(positionX, positionY, positionZ), (checkpoint, someUint) =>
            {
                var vehicle = Nova.v.GetVehicle(player.setup.driver.vehicle.VehicleDbId);
                vehicle.isStowed = true;
                vehicle.SaveAndDestroy();
            });
            player.CreateVehicleCheckpoint(orangeGaragePoint);
        }

        private void garagePanel(Player player)
        {
            UIPanel garagePanel = new UIPanel("garagePanel", UIPanel.PanelType.Tab);
            garagePanel.SetTitle("<color=#156884>Garage</color>");
            if (Nova.v == null || Nova.v.vehicles == null) { return; }

            PointData nearestPoint = points
                .Where(p => p.Type == "Spawn" && p.PlayerId == player.character.Id)
                .OrderBy(p => Vector3.Distance(new Vector3(p.X, p.Y, p.Z), player.setup.transform.position))
                .FirstOrDefault();

            if (nearestPoint == null) { return; }

            Vector3 spawnPosition = new Vector3(nearestPoint.SpawnX, nearestPoint.SpawnY, nearestPoint.SpawnZ);
            Quaternion spawnRotation = Quaternion.Euler(0, 90, 0);

            foreach (var v in Nova.v.vehicles)
            {
                if (v == null) continue;
                if (v.permissions?.owner == null || v.permissions.owner.characterId != player.character.Id) continue;

                string stowed = v.isStowed ? "<color=#B63637>Rangé</color>" : "<color=#3E9F18>Sorti</color>";

                garagePanel.AddTabLine($"Plaque: {v.plate}    -    {stowed}", _ =>
                {
                    Nova.v.UnstowVehicle(v.vehicleId, spawnPosition, spawnRotation);
                    player.ClosePanel(garagePanel);
                });
            }
            garagePanel.AddButton("<color=#3E9F18>Sélectionner</color>", _ =>
            {
                garagePanel.SelectTab();
                player.ClosePanel(garagePanel);
            });
            garagePanel.AddButton("<color=#B63637>Fermer</color>", _ => player.ClosePanel(garagePanel));
            player.ShowPanelUI(garagePanel);
        }

        private void SavePoints()
        {
            try
            {
                string json = JsonConvert.SerializeObject(points, Formatting.Indented);
                File.WriteAllText(filePath, json);
                Debug.Log("\u001b[32mPoints Sauvegardés !\u001b[0m");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Erreur lors de la sauvegarde des points : {ex.Message}");
            }
        }

        private void LoadPoints(Player player)
        {
            if (!File.Exists(filePath)) { return; }

            try
            {
                string json = File.ReadAllText(filePath);
                points = JsonConvert.DeserializeObject<List<PointData>>(json) ?? new List<PointData>();

                foreach (var point in points)
                {
                    if (point.Type == "Stow")
                    {
                        NVehicleCheckpoint vehiclePoint = new NVehicleCheckpoint(player.netId, new Vector3(point.X, point.Y, point.Z), (checkpoint, someUint) =>
                        {
                            var vehicle = Nova.v.GetVehicle(player.setup.driver.vehicle.VehicleDbId);
                            if (vehicle != null)
                            {
                                vehicle.isStowed = true;
                                vehicle.SaveAndDestroy();
                            }
                        });
                        player.CreateVehicleCheckpoint(vehiclePoint);
                    }
                    else if (point.Type == "Spawn")
                    {
                        NCheckpoint playerPoint = new NCheckpoint(player.netId, new Vector3(point.X, point.Y, point.Z), _ => garagePanel(player));
                        player.CreateCheckpoint(playerPoint);
                    }
                }
                Debug.Log($"{points.Count} points chargé pour {player.FullName}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Erreur lors du chargement des points : {ex.Message}");
            }
        }

        private class PointData
        {
            public string Type { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
            public float PlayerId { get; set; }

            public float SpawnX { get; set; }
            public float SpawnY { get; set; }
            public float SpawnZ { get; set; }
        }
    }
}