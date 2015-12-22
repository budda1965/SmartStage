﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartStage
{

	public struct DState
	{
		public double vx;
		public double vy;
		public double ax;
		public double ay;
		public double dm;
		//For plot
		public double ax_nograv;
		public double ay_nograv;
	}

	public class SimulationState
	{
		public double x;
		public double y;
		public double vx;
		public double vy;
		public double m;
		private float minThrust;
		private float maxThrust;
		public float throttle;
		public List<EngineWrapper> activeEngines;
		public Dictionary<Part,Node> availableNodes;
		public CelestialBody planet;
		private DefaultAscentPath ascentPath;

		public double maxAcceleration;
		public Vector3d forward;

		public SimulationState(CelestialBody planet, double departureAltitude, Vector3d forward)
		{
			this.planet = planet;
			this.forward = forward;

			x = 0;
			y = planet.Radius + departureAltitude;
			vx = planet.rotates ? (y * 2 * Math.PI / planet.rotationPeriod) : 0;
			vy = 0;
			throttle = 1.0f;
			activeEngines = new List<EngineWrapper>();
			availableNodes = new Dictionary<Part, Node>();
			ascentPath = new DefaultAscentPath(planet);
		}

		public SimulationState increment(DState delta, double dt)
		{
			SimulationState res = (SimulationState) MemberwiseClone();
			res.x += dt * delta.vx;
			res.y += dt * delta.vy;
			res.vx += dt * delta.ax;
			res.vy += dt * delta.ay;
			res.m += dt * delta.dm;
			if (r2 <= res.planet.Radius * res.planet.Radius)
			{
				double r = res.r;
				res.x *= res.planet.Radius / r;
				res.y *= res.planet.Radius / r;
				res.vx = res.y * (planet.rotates ? (2 * Math.PI / planet.rotationPeriod) : 0);
				res.vy = - res.x * (planet.rotates ? (2 * Math.PI / planet.rotationPeriod) : 0);
			}
			return res;
		}

		public List<Part> updateEngines()
		{
			List<Part> activeParts = new List<Part>();
			activeEngines.Clear();
			foreach(Node node in availableNodes.Values)
			{
				if ((node.isActiveEngine(availableNodes) && ! node.isSepratron))
				{
					activeEngines.AddRange(node.part.Modules.OfType<ModuleEngines>().Select(e => new EngineWrapper(e, availableNodes)));
					activeParts.Add(node.part);
				}
			}
			minThrust = activeEngines.Sum(e => e.thrust(0, pressure, machNumber, atmDensity));
			maxThrust = activeEngines.Sum(e => e.thrust(1, pressure, machNumber, atmDensity));
			return activeParts;
		}
		public double r { get { return Math.Sqrt(x * x + y * y);}}
		public double r2 { get { return x * x + y * y;}}

		// unit vectors
		private double u_x { get { return x/r;}}
		private double u_y { get { return y/r;}}

		public double v_surf_x { get { return vx - u_y * (planet.rotates ? (2 * Math.PI * r / planet.rotationPeriod) : 0);}}
		public double v_surf_y { get { return vy + u_x * (planet.rotates ? (2 * Math.PI * r / planet.rotationPeriod) : 0);}}

		public double v_surf { get { return Math.Sqrt(v_surf_x * v_surf_x + v_surf_y * v_surf_y);}}

		public float pressure { get { return (float)FlightGlobals.getStaticPressure(r - planet.Radius, planet);}}

		public float machNumber { get {
				double soundSpeed = planet.GetSpeedOfSound(pressure, atmDensity);
				double mach = v_surf / soundSpeed;
				if (mach > 25.0) { mach = 25.0; }
				return (float)mach;}}

		public float atmDensity { get { return (float)StockAeroUtil.GetDensity(r - planet.Radius, planet);}}

		public DState derivate()
		{
			DState res = new DState();
			res.vx = vx;
			res.vy = vy;

			double r = this.r;
			float altitude = (float) (r - planet.Radius);

			double theta = Math.Atan2(u_x, u_y);
			double thrustDirection = theta + ascentPath.FlightPathAngle(altitude);

			// gravity
			double grav_acc = -planet.gravParameter / (r * r);

			// drag
			var dragForce = StockAeroUtil.SimAeroForce(availableNodes.Keys.ToList(), planet, new UnityEngine.Vector3(0,(float)v_surf, 0), altitude);
			double drag_acc = dragForce.magnitude / m;

			//throttle
			double desiredThrust = maxAcceleration * m;
			if (maxThrust != minThrust)
				throttle = ((float)desiredThrust - minThrust) / (maxThrust - minThrust);
			else
				throttle = 1;
			throttle = Math.Max(0, Math.Min(1, throttle));

			// Effective thrust
			double F = activeEngines.Sum(e => e.thrust(throttle, pressure, machNumber, atmDensity));

			// Propellant mass variation
			res.dm = - activeEngines.Sum(e => e.evaluateFuelFlow(atmDensity, machNumber, throttle));

			res.ax_nograv = F / m * Math.Sin(thrustDirection);
			res.ay_nograv = F / m * Math.Cos(thrustDirection);
			if (v_surf != 0)
			{
				res.ax_nograv -= drag_acc * v_surf_x/v_surf;
				res.ay_nograv -= drag_acc * v_surf_y/v_surf;
			}
			res.ax = res.ax_nograv + grav_acc * u_x;
			res.ay = res.ay_nograv + grav_acc * u_y;

			return res;
		}
	}
}

