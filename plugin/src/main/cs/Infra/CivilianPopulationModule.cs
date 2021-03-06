﻿using System;
using System.Collections.Generic;
using System.Linq;
using CivilianPopulation.Domain;
using CivilianPopulation.Domain.Repository;
using CivilianPopulation.Domain.Services;
using CivilianPopulation.GUI;
using UnityEngine;

namespace CivilianPopulation.Infra
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.FLIGHT, GameScenes.TRACKSTATION, GameScenes.SPACECENTER, GameScenes.EDITOR)]
    public class CivilianPopulationModule : ScenarioModule
    {
        private static CivPopKerbalBuilder builder;
        private static CivPopServiceContractors contractors;
        private static CivPopServiceDeath death;
        private static CivPopServiceGrowth growth;
        private static CivPopServiceRent rent;

        [KSPField(isPersistant = true, guiActive = false)]
        public string repoJSON;

        private static CivilianPopulationGUI gui;

        protected System.Random rng;

        public void Start()
        {
            if (builder == null)
            {
                builder = new CivPopKerbalBuilder(this.GenerateKerbalName);
            }
            if (contractors == null)
            {
                contractors = new CivPopServiceContractors(builder);
            }
            if (death == null)
            {
                death = new CivPopServiceDeath();
            }
            if (growth == null)
            {
                growth = new CivPopServiceGrowth(builder);
            }
            if (rent == null)
            {
                rent = new CivPopServiceRent();
            }
            if (gui == null)
            {
                gui = new CivilianPopulationGUI(rent);
            }
            this.rng = new System.Random();
        }

        public void OnGUI()
        {
            gui.update(Planetarium.GetUniversalTime(), this.GetRepository());
        }

        public void FixedUpdate()
        {
            double now = Planetarium.GetUniversalTime();

            CivPopRepository repo = GetRepository();
            this.UpdateRepository(repo);

            contractors.Update(now, repo);
            death.Update(now, repo);
            growth.Update(now, repo);

            if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
            {
                rent.Update(now, repo);
            }

            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel != null)
            {
                KillKerbals(repo, vessel);
                CreateKerbals(repo, vessel);
            }
            this.repoJSON = repo.ToJson();
        }

        private string GenerateKerbalName(CivPopKerbalGender gender)
        {
            string res;
            if (CivPopKerbalGender.MALE.Equals(gender))
            {
                res = CrewGenerator.GetRandomName(ProtoCrewMember.Gender.Male);
            }
            else
            {
                res = CrewGenerator.GetRandomName(ProtoCrewMember.Gender.Female);
            }
            return res;
        }

        private CivPopRepository GetRepository()
        {
            CivPopRepository repo = new CivPopRepository();
            if (this.repoJSON != null)
            {
                repo = new CivPopRepository(this.repoJSON);
            }
            return repo;
        }

        private void UpdateRepository(CivPopRepository repo)
        {
            ProtoCrewMember.KerbalType type = ProtoCrewMember.KerbalType.Crew;
            //ProtoCrewMember.KerbalType.Applicant;
            //ProtoCrewMember.KerbalType.Crew;
            //ProtoCrewMember.KerbalType.Tourist;
            //ProtoCrewMember.KerbalType.Unowned;

            ProtoCrewMember.RosterStatus[] statuses = {
                ProtoCrewMember.RosterStatus.Assigned,
                ProtoCrewMember.RosterStatus.Available,
                ProtoCrewMember.RosterStatus.Dead,
                ProtoCrewMember.RosterStatus.Missing
            };
            IEnumerable<ProtoCrewMember> kerbals = HighLogic.CurrentGame.CrewRoster.Kerbals(type, statuses);


            foreach (ProtoCrewMember kerbal in kerbals)
            {
                CivPopKerbal civKerbal;
                if (!repo.KerbalExists(kerbal.name))
                {
                    string kerbalName = kerbal.name;
                    CivPopKerbalGender gender = CivPopKerbalGender.FEMALE;
                    if (ProtoCrewMember.Gender.Male.Equals(kerbal.gender))
                    {
                        gender = CivPopKerbalGender.MALE;
                    }
                    double birthdate = Planetarium.GetUniversalTime() - 15 * TimeUnit.YEAR - rng.Next(15 * TimeUnit.YEAR);
                    civKerbal = new CivPopKerbal(kerbalName, gender, birthdate, false);
                } else {
                    civKerbal = repo.GetKerbal(kerbal.name);
                }
                bool civilian = false;
                if ("Civilian".Equals(kerbal.trait))
                {
                    civilian = true;
                }
                civKerbal.SetCivilian(civilian);
                repo.Add(civKerbal);
            }

            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                CivPopVessel civVessel;
                if (!repo.VesselExists(vessel.id.ToString()))
                {
                    civVessel = new CivPopVessel(vessel.id.ToString());
                }
                else
                {
                    civVessel = repo.GetVessel(vessel.id.ToString());
                }
                civVessel.SetOrbiting(!vessel.LandedOrSplashed);
                civVessel.SetBody(new Domain.CelestialBody(vessel.mainBody.name, GetBodyType(vessel.mainBody)));

                foreach (VesselModule module in vessel.vesselModules)
                {
                    if (module.GetType() == typeof(CivilianPopulationVesselModule))
                    {
                        //log("vessel has civ pop module");
                        CivilianPopulationVesselModule civModule = (CivilianPopulationVesselModule)module;
                        civVessel.SetCapacity(civModule.capacity);
                        civVessel.SetAllowDocking(civModule.allowDocking);
                        civVessel.SetAllowBreeding(civModule.allowBreeding);
                    }
                }

                foreach (ProtoCrewMember kerbal in vessel.GetVesselCrew())
                {
                    CivPopKerbal civKerbal = repo.GetKerbal(kerbal.name);
                    civKerbal.SetVesselId(vessel.id.ToString());
                }
                repo.Add(civVessel);
            }

            foreach (CivPopVessel civVessel in repo.GetVessels()) 
            {
                bool found = false;
                foreach (Vessel vessel in FlightGlobals.Vessels)
                {
                    if (vessel.id.ToString().Equals(civVessel.GetId()))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    repo.Remove(civVessel);
                }
            }
        }

        private Domain.CelestialBodyType GetBodyType(CelestialBody body)
        {
            Domain.CelestialBodyType type = Domain.CelestialBodyType.OTHERS;
            if (body.isHomeWorld)
            {
                type = Domain.CelestialBodyType.HOMEWORLD;
            }
            else if (body.orbit != null
                   && body.orbit.referenceBody != null
                   && body.orbit.referenceBody.isHomeWorld)
            {
                type = Domain.CelestialBodyType.HOMEWORLD_MOON;
            }
            return type;
        }

        private void KillKerbals(CivPopRepository repo, Vessel vessel)
        {
            foreach (CivPopKerbal current in repo.GetDeadRosterForVessel(vessel.id.ToString()))
            {
                Part part = null;
                foreach (Part p in vessel.parts)
                {
                    foreach (ProtoCrewMember crew in p.protoModuleCrew)
                    {
                        if (crew.name.Equals(current.GetName()))
                        {
                            part.RemoveCrewmember(crew);
                            vessel.RemoveCrew(crew);
                            crew.Die();
                        }
                    }
                }
                repo.Remove(current);
            }
        }

        private void CreateKerbals(CivPopRepository repo, Vessel vessel)
        {
            foreach (CivPopKerbal current in repo.GetLivingRosterForVessel(vessel.id.ToString()))
            {
                ProtoCrewMember crew = vessel.GetVesselCrew().Find(c => c.name.Equals(current.GetName()));
                if (crew == null)
                {
                    List<CivilianPopulationHousingModule> houses = vessel.FindPartModulesImplementing<CivilianPopulationHousingModule>();
                    if (houses.Count > 0)
                    {
                        foreach (CivilianPopulationHousingModule house in houses)
                        {
                            if (house.part.CrewCapacity > house.part.protoModuleCrew.Count)
                            {
                                KerbalRoster kspRoster = HighLogic.CurrentGame.CrewRoster;
                                ProtoCrewMember newKerbal = kspRoster.GetNewKerbal(ProtoCrewMember.KerbalType.Crew);

                                ProtoCrewMember.Gender gender = ProtoCrewMember.Gender.Male;
                                if (current.GetGender().Equals(CivPopKerbalGender.FEMALE))
                                {
                                    gender = ProtoCrewMember.Gender.Female;
                                }

                                while (newKerbal.gender != gender)
                                {
                                    kspRoster.Remove(newKerbal);
                                    newKerbal = kspRoster.GetNewKerbal(ProtoCrewMember.KerbalType.Crew);
                                }
                                newKerbal.ChangeName(current.GetName());
                                newKerbal.trait = "Civilian";

                                if (house.part.AddCrewmember(newKerbal))
                                {
                                    vessel.SpawnCrew();
                                    log("CreateKerbals : " + newKerbal.name + " has been placed successfully");
                                    break;
                                }
                            }
                        }
                    }
                }
                crew = vessel.GetVesselCrew().Find(c => c.name.Equals(current.GetName()));
                if (crew == null)
                {
                    current.SetDead(true);
                    log("CreateKerbals : " + current.GetName() + " died because of a lack of room");
                }
            }
        }

		private void log(string message)
		{
			Debug.Log(this.GetType().Name + " - " + message);
		}
    }
}
