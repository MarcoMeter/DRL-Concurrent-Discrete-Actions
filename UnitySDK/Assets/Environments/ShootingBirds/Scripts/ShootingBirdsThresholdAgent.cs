﻿using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MLAgents;

public class ShootingBirdsThresholdAgent : Agent
{
    #region Member Fields
    private ShootingBirdsEnvironment _environment;
    private const string _BIRD_TAG = "Bird";
    private const string _ENV_TAG = "Environment";
    private int _leftAmmo = 8;
    private Vector3 _origin;
    [Header("UI")]
    [SerializeField]
    private Text _accuracyText;
    [SerializeField]
    private Text _ammoText;
    private int _shotCount = 0;
    private int _hitCount = 0;
    private int _reloads = 0;
    private int _reloadsOnEmpty = 0;
    [SerializeField]
    private Text _averageVelocityText;
    private float _sumVelocityX = 0.0f;
    private float _sumVelocityY = 0.0f;
    private int _stepDevisor = 0;
    [Header("Action Space")]
    [SerializeField]
    private float _discreteActionThreshold = 0.1f;
    [SerializeField]
    private Rigidbody2D _rigidbody;
    [SerializeField]
    private float _movementSpeed = 10.0f;
    [SerializeField]
    private int _maxAmmo = 8;
    [SerializeField]
    private bool _infinteAmmo = true;
    [Header("State Space")]
    [SerializeField]
    private int _numVisionRays = 16;
    [SerializeField]
    private float _visionRayLength = 5.0f;
    private float _angleStep;
    private List<Ray2D> _rays;
    [SerializeField]
    private LayerMask _layerMaskBird;
    [SerializeField]
    private LayerMask _layerMaskBirdEnv;
    #endregion

    #region Unity ML-Agents
    /// <summary>
    /// Saves the position of the agent.
    /// </summary>
    public override void InitializeAgent()
    {
        _origin = transform.position;
        // Initialize rays for the agent's input
        _angleStep = 360.0f / _numVisionRays;
    }

    /// <summary>
    /// Sets the agent back to his initial position.
    /// </summary>
    public override void AgentReset()
    {
        transform.position = _origin;
        _hitCount = _shotCount = _reloads = _reloadsOnEmpty = 0;
        _leftAmmo = _maxAmmo;
        _sumVelocityX = 0.0f;
        _sumVelocityY = 0.0f;
        _stepDevisor = 0;
    }

    /// <summary>
    /// Observes the state space.
    /// </summary>
    public override void CollectObservations()
    {
        // Remaining ammunation
        AddVectorObs(_leftAmmo / _maxAmmo);                         // 1
        // Relative position to the origin
        AddVectorObs((transform.position.x - _origin.x) / 17.715f); // 1
        AddVectorObs((transform.position.y - _origin.y) / 10.215f); // 1
        // Velocity of the agent
        AddVectorObs(_rigidbody.velocity.normalized);               // 2
        // Distances to spotted birds (-1.0 if nothing is spotted)
        AddVectorObs(SenseSurroundings());                          // 24 (numVisionRays)
        // Check what's being hovered
        AddVectorObs(SenseHoveredEntity());
    }

    /// <summary>
    /// Executes actions for movement, shooting and reloading.
    /// </summary>
    public override void AgentAction(float[] vectorAction, string textAction)
    {
        if (brain.brainParameters.vectorActionSpaceType.Equals(SpaceType.continuous))
        {
            // Move
            _rigidbody.velocity = new Vector2(vectorAction[0] * _movementSpeed, vectorAction[1] * _movementSpeed);
            // Shoot
            if (Mathf.Abs(vectorAction[2]) < _discreteActionThreshold)
            {
                Shoot();
            }
            // Reload
            if (Mathf.Abs(vectorAction[3]) < _discreteActionThreshold)
            {
                Reload();
            }
        }

        // Update speed UI
        _stepDevisor++;
        _sumVelocityX += Math.Abs(_rigidbody.velocity.x);
        _sumVelocityY += Math.Abs(_rigidbody.velocity.y);
        _averageVelocityText.text = "|Average speed|: " + (_sumVelocityX / _stepDevisor).ToString("0.00") + " | " + (_sumVelocityY / _stepDevisor).ToString("0.00");
    }
    #endregion

    #region Unity Lifecycle
    /// <summary>
    /// Used for player inputs.
    /// </summary>
    private void Update()
    {
 
        if (brain.brainType.Equals(BrainType.Player))
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                Shoot();
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                Reload();
            }
        }
    }

    /// <summary>
    /// Used for player inputs.
    /// </summary>
    private void FixedUpdate()
    {
        if(brain.brainType.Equals(BrainType.Player))
        {
            _rigidbody.velocity = new Vector2(Input.GetAxis("Horizontal") * _movementSpeed, Input.GetAxis("Vertical") * _movementSpeed);
        }
    }
    #endregion

    #region Private Functions
    /// <summary>
    /// Shoots at the position of the agent, if enough ammo is left.
    /// </summary>
    private void Shoot()
    {
        if(_leftAmmo > 0 || _infinteAmmo)
        {
            _shotCount++;

            Collider2D coll = Physics2D.OverlapPoint((Vector2)transform.position, _layerMaskBird);
            // Check what's been shot
            if(coll && coll.tag.Equals(_BIRD_TAG))
            {
                BirdBehavior bird = coll.GetComponent<BirdBehavior>();
                bird.Hit();
                // Reward the agent for hitting a bird based on the bird's size
                switch(bird.BirdSize)
                {
                    case BirdSize.S:
                        AddReward(1.0f);
                        break;
                    case BirdSize.M:
                        AddReward(0.5f);
                        break;
                    case BirdSize.L:
                        AddReward(0.25f);
                        break;
                }
                _hitCount++;
            }
            else
            {
                // Punish for hitting nothing
                AddReward(-0.1f);
            }

            if (!_infinteAmmo)
            {
                // Decrease ammo
                _leftAmmo--;
            }
        }
        else
        {
            // Punish the agent for trying to shoot without ammo
            AddReward(-1f);
        }
        // Update UI
        _accuracyText.text = "Shot Accuracy: " + ((float)_hitCount / (float)_shotCount).ToString("0.00");
        _ammoText.text = "Ammo: " + _leftAmmo + " / " + _maxAmmo;
    }

    /// <summary>
    /// Refills the ammo at some cost.
    /// </summary>
    private void Reload()
    {
        _reloads++;
        // Punish the agent for reloading if it has ammo left
        if(_leftAmmo > 0)
        {
            AddReward(-0.1f);
        }
        else
        {
            _reloadsOnEmpty++;
        }
        _leftAmmo = _maxAmmo;
        // Update UI
        _ammoText.text = "Ammo: " + _leftAmmo + " / " + _maxAmmo;
    }

    /// <summary>
    /// Fire raycasts to observe the agent's surroundings.
    /// </summary>
    /// <returns></returns>
    private List<float> SenseSurroundings()
    {
        List<float> observation = new List<float>();

        // Update agent's vision
        _rays = new List<Ray2D>();
        for (int i = 0; i < _numVisionRays; i++)
        {
            Vector2 circumferencePoint = new Vector2(transform.position.x + (_visionRayLength * Mathf.Cos(((_angleStep * i)) * Mathf.Deg2Rad)),
                                            transform.position.y + (_visionRayLength * Mathf.Sin((transform.rotation.eulerAngles.z + (_angleStep * i)) * Mathf.Deg2Rad)));
            _rays.Add(new Ray2D((Vector2)transform.position, (circumferencePoint - (Vector2)transform.position).normalized));
        }

        // Execute raycasts to query the agent's vision (1 inputs per raycast)
        foreach (var ray in _rays)
        {
            RaycastHit2D hit = Physics2D.Raycast(ray.origin, ray.direction, _visionRayLength, _layerMaskBird);
            if (hit)
            {
                observation.Add(hit.distance / _visionRayLength);
            }
            else
            {
                // if no bird is spotted
                observation.Add(-1.0f);
            }

            Debug.DrawLine(ray.origin, ray.origin + ray.direction * _visionRayLength, Color.red, 0.0f); // Check correct behavior of raycasts
        }
        return observation;
    }

    /// <summary>
    /// Uses 2D Physics to check what entity is hovered by the agent.
    /// </summary>
    /// <returns>Returns 1.0f for a bird being sensed, 0.5f for the environment and 0.0f for nothing.</returns>
    private float SenseHoveredEntity()
    {
        Collider2D coll = Physics2D.OverlapPoint((Vector2)transform.position, _layerMaskBirdEnv);

        if(coll)
        {
            switch(coll.tag)
            {
                case _BIRD_TAG:
                    return 1.0f;
                case _ENV_TAG:
                    return 0.5f;
            }
        }
        return 0.0f;
    }
    #endregion

    #region Public Functions
    public float GetClickAccuracy()
    {
        if (_shotCount <= 0)
            return 0;

        return ((float)_hitCount / (float)_shotCount);
    }

    public float GetReloadAccuracy()
    {
        if (_reloads <= 0)
            return 0;

        return ((float)_reloadsOnEmpty / (float)_reloads);
    }
    #endregion
}