using UnityEngine;
using StarWars.Actions;
using Infra.Utils;

namespace StarWars.Brains {
	public class ClinggerBrain : SpaceshipBrain {
	    public override string DefaultName {
	        get {
	            return "Clingger";
	        }
	    }
	    public override Color PrimaryColor {
	        get {
	            return new Color((float)0xF4 / 0xFF, (float)0x77 / 0xFF, (float)0x33 / 0xFF, 1f);
	        }
	    }
	    public override SpaceshipBody.Type BodyType {
	        get {
	            return SpaceshipBody.Type.XWing;
	        }
	    }

		// Change to "public" for adjusting/debugging parameters
		private float _timeToEstimateShoot = 80f;
		private int _framesToKeepTaregt = 100;
		private int _framesToShieldShot = 20; 
		private int _framesToShieldSpaceship = 40; 

		// Counters & "memory"
		private int _framesToKeepShieldCounter = 0; 
		private int _framesToKeepTargetCounter = 0;
		private Spaceship _target = null;
		private Spaceship _collidingWith = null;
		private Shot _shieldingFrom = null;
		private bool _aliveLastFrame = false;

		// constants
		private static float FAILURE = -1f;

		protected void Awake(){
			_ResetValues ();
		}


		protected void Update(){
			_framesToKeepTargetCounter = (_framesToKeepTargetCounter + 1) % _framesToKeepTaregt;
			_framesToKeepShieldCounter = (int)(Mathf.Max ((float)_framesToKeepShieldCounter - 1f, 0f));

			// Since barins are not re-awoken not re-enabled on revival, This replaces "Awake"
			if (!_aliveLastFrame && spaceship != null && spaceship.IsAlive) {
				_ResetValues ();
			}
			_aliveLastFrame = spaceship == null ? false : spaceship.IsAlive;
		}

		// Since barins are not re-awoken nor re-enabled on revival, This replaces "Awake"
		private void _ResetValues(){
			_target = null;
			_shieldingFrom = null;
			_framesToKeepTargetCounter = 0;
			_framesToKeepShieldCounter = 0;
		}

	    /***
	     * Next action of Spaceship brain follows the following logic (with this very priority): 
	     * 	Shield against shots that are estimated to hurt me
	     * 	Remove shield if not necessary
	     * 	If enroute towards collision with other spaceship & shield is up - do nothing.
	     * 	Find closest target and keep it for a while (_framesToKeepTargetCounter updates)
	     * 	Raise shield if enroute towards collision with other SpaceShip
	     * 	Move towards current target's general position
	     * 	Shoot target if possible & if estimated hit
	     */
		public override Action NextAction() {

			// Defend against shots
			foreach(Shot s in Space.Shots){
				float timeToCollide = _EstimateCollision (spaceship, Spaceship.SPEED_PER_TURN, s, Shot.SPEED_PER_TURN, 3f);
				if (timeToCollide != FAILURE) {
					Action action = _RaiseShieldFor(_framesToShieldShot);
					if (action != null) {
						_shieldingFrom = s;
						return action;
					}
				}
			}
				
			// Stop shield if shot is dead
			if(_shieldingFrom != null && !_shieldingFrom.IsAlive && spaceship.IsShieldUp && _framesToKeepShieldCounter > 0){
				return ShieldDown.action;
			}
				

			// Stay on target towards collision, Keep Shield on & shoot if possible
			if (_framesToKeepShieldCounter > 0 && _collidingWith != null) {
				return DoNothing.action;
			}

				
        	// Find a new target.
			if (_framesToKeepTargetCounter == 0 || _target == null || !_target.IsAlive) {
				_target = _LocateClosestShip ();
				_collidingWith = null;
        	}
				

			// If about to kill with shield, raise it and wait to collision
			float timeToCollideWithTarget = _EstimateCollision (spaceship, Spaceship.SPEED_PER_TURN, _target, Spaceship.SPEED_PER_TURN, 4f);
			if (timeToCollideWithTarget != FAILURE) {
				Action action = _RaiseShieldFor (_framesToShieldSpaceship);
				if (action != null) {
					_collidingWith = _target;
					return action;
				}
			} else {
				_collidingWith = null;
			}

			// Move toawrds target
        	if (_target != null) {
	            var pos = spaceship.ClosestRelativePosition(_target);
	            var forwardVector = spaceship.Forward;
	            var angle = pos.GetAngle(forwardVector);
	            if (angle >= 20) return TurnLeft.action;
	            if (angle <= -20) return TurnRight.action;
	        }

			return _TryToShoot (_target);
    	}

		/***
		 * Locates the closest SpaceShip and returns it. 
		 * if no such ship, returns null
		 */
		private Spaceship _LocateClosestShip(){
			Spaceship closest = null;
			float minDistance = Mathf.Infinity;
			foreach (Spaceship s in Space.Spaceships) {
				float distance = spaceship.ClosestRelativePosition (s).magnitude;
				if (s != spaceship && distance < minDistance) {
					minDistance = distance;
					closest = s;
				}
			}
			return closest;
		}


		/***
		 * Given 2 SpaceObbjects (self & other) returns their predicted time of collision
		 * after timeToCheck, if such exists & assuming they maintain their direction and speed. 
		 * Otherwise returns FAILURE.
		 */
		private float _EstimateCollision(SpaceObject self, float selfSpeed, SpaceObject other, float otherSpeed, float timeToCheck=10f){
			if (other == null) {
				return FAILURE;
			}
			float T0 = Time.time;
			float RA = self.Radius; // radius of A
			float RB = other.Radius; // radius of B
			Vector2 VA = self.Forward * selfSpeed; // velocity vector of A
			Vector2 VB = other.Forward * otherSpeed; // velocity vector of B
			Vector2 PA0 = spaceship.Position; // position of A at time 0
			Vector2 PB0 = other.Position; // position of B at time 0
			for(float t = 0f; t <= timeToCheck; t += 0.3f){
				float testedTime = T0 + (float)t;
				Vector2 PA_T = _PositionAtTime (PA0, VA, T0, testedTime);
				Vector2 PB_T = _PositionAtTime (PB0, VB, T0, testedTime);

				// If expected collision, return time of collision
				if ((PA_T - PB_T).magnitude <= (RA + RB)) {
					return testedTime;
				}
			}
			return FAILURE;
		}

		private Vector2 _PositionAtTime(Vector2 PositionAtTimeZero, Vector2 VelocityVector, float timeZero, float timeToCheck){
			return PositionAtTimeZero + (VelocityVector * (timeToCheck - timeZero));
		}

		/**
		 * Raise shield for given amount of frames. 
		 */
		private Action _RaiseShieldFor(int frames){
			if (spaceship.CanRaiseShield && !spaceship.IsShieldUp) {
				_framesToKeepShieldCounter = frames;
				return ShieldUp.action;
			} else if (_framesToKeepShieldCounter == 0 && spaceship.IsShieldUp) {
				return ShieldDown.action;
			} else {
				return DoNothing.action;
			}
		}

		/***
		 * Shoot target if shooting is currently possible & an estimated hit is expected
		 * otherwise does nothing.
		 */
		private Action _TryToShoot(Spaceship other){
			if (other == null) {
				return DoNothing.action;
			}
			float timeOfHit = _EstimateCollision (spaceship, Shot.SPEED_PER_TURN, other, Spaceship.SPEED_PER_TURN, _timeToEstimateShoot);
			if (spaceship.CanShoot && timeOfHit != FAILURE && !other.IsShieldUp) {
				return Shoot.action;
			} 
			return DoNothing.action;
		}

	}
}