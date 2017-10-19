using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Linq;
using System.Collections.Generic;

[RequireComponent(typeof(Animator))]
public partial class Pet : Entity {
    [SyncVar] GameObject _owner;
    public Player owner {
        get { return _owner != null  ? _owner.GetComponent<Player>() : null; }
        set { _owner = value != null ? value.gameObject : null; }
    }

    [Header("Text Meshes")]
    public TextMesh ownerNameOverlay;

    // level based stats
    [System.Serializable]
    public partial class PetLevel {
        public int healthMax = 100;
        public long experienceMax = 10;
        public int baseDamage = 1;
        public int baseDefense = 1;
        [Range(0, 1)] public float baseBlockChance;
        [Range(0, 1)] public float baseCriticalChance;
    }
    [Header("Level based Stats")]
    public PetLevel[] levels = { new PetLevel() }; // default

    public override int healthMax { get { return levels[level-1].healthMax; } }
    public override int manaMax { get { return 0; } } // pet's don't need mana. less to sync in item, etc.
    public override int damage { get { return levels[level-1].baseDamage; } }
    public override int defense { get { return levels[level-1].baseDefense; } }
    public override float blockChance { get { return levels[level-1].baseBlockChance; } }
    public override float criticalChance { get { return levels[level-1].baseCriticalChance; } }

    [Header("Experience")] // note: int is not enough (can have > 2 mil. easily)
    [SyncVar, SerializeField] long _experience = 0;
    public long experience {
        get { return _experience; }
        set {
            if (value <= _experience) {
                // decrease
                _experience = Math.Max(value, 0);
            } else {
                // increase with level ups
                // set the new value (which might be more than expMax)
                _experience = value;

                // now see if we leveled up (possibly more than once too)
                // (can't level up if already max level)
                while (_experience >= experienceMax && level < levels.Length) {
                    // subtract current level's required exp, then level up
                    _experience -= experienceMax;
                    ++level;

                    // keep player's pet item up to date
                    SyncToOwnerPetItem();

                    // addon system hooks
                    Utils.InvokeMany(typeof(Pet), this, "OnLevelUp_");
                }

                // set to expMax if there is still too much exp remaining
                if (_experience > experienceMax) _experience = experienceMax;
            }
        }
    }
    public long experienceMax { get { return levels[level-1].experienceMax; } }

    [Header("Movement")]
    public float returnDistance = 25; // return to player if dist > ...
    // pets should follow their targets even if they run out of the movement
    // radius. the follow dist should always be bigger than the biggest archer's
    // attack range, so that archers will always pull aggro, even when attacking
    // from far away.
    public float followDistance = 20;
    // pet should teleport if the owner gets too far away for whatever reason
    public float teleportDistance = 30;

    [Header("Death")]
    public float deathTime = 2; // enough for animation
    float deathTimeEnd;
    public long revivePrice = 10;

    [Header("Behaviour")]
    [SyncVar] public bool defendOwner = true; // attack what attacks the owner
    [SyncVar] public bool autoAttack = true; // attack what the owner attacks

    // sync with owner's pet item //////////////////////////////////////////////
    // to save computations we don't sync to it all the time, it's enough to
    // sync in:
    // * OnDestroy when unsummoning the pet
    // * On experience gain so that level ups and exp are saved properly
    // * OnDeath so that people can't cheat around reviving pets
    // => after a server crash the health/mana might not be exact, but that's a
    //    good price to pay to save computations in each Update tick
    [Server]
    public void SyncToOwnerPetItem() {
        // owner might be null if server shuts down and owner was destroyed before
        if (owner != null) {
            // find the item
            int index = owner.inventory.FindIndex(item => item.valid && item.petSummoned == gameObject);
            if (index != -1) {
                Item item = owner.inventory[index];
                item.petHealth = health;
                item.petLevel = level;
                item.petExperience = experience;
                owner.inventory[index] = item;
                Debug.LogWarning("Pet synced to owner item");
            } else Debug.LogWarning("Pet(" + name + ") not found in owners(" + owner.name + ") inventory");
        }
    }

    // networkbehaviour ////////////////////////////////////////////////////////
    protected override void Awake() {
        base.Awake();

        // addon system hooks
        Utils.InvokeMany(typeof(Pet), this, "Awake_");
    }

    public override void OnStartServer() {
        // call Entity's OnStartServer
        base.OnStartServer();

        // load skills based on skill templates
        foreach (var template in skillTemplates)
            skills.Add(new Skill(template));

        // addon system hooks
        Utils.InvokeMany(typeof(Pet), this, "OnStartServer_");
    }

    protected override void Start() {
        base.Start();

        // addon system hooks
        Utils.InvokeMany(typeof(Pet), this, "Start_");
    }

    void LateUpdate() {
        // pass parameters to animation state machine
        // => passing the states directly is the most reliable way to avoid all
        //    kinds of glitches like movement sliding, attack twitching, etc.
        // => make sure to import all looping animations like idle/run/attack
        //    with 'loop time' enabled, otherwise the client might only play it
        //    once
        // => only play moving animation while the agent is actually moving. the
        //    MOVING state might be delayed to due latency or we might be in
        //    MOVING while a path is still pending, etc.
        if (isClient) { // no need for animations on the server
            animator.SetBool("MOVING", state == "MOVING" && agent.velocity != Vector3.zero);
            animator.SetBool("CASTING", state == "CASTING");
            animator.SetInteger("currentSkill", currentSkill);
            animator.SetBool("DEAD", state == "DEAD");
        }

        // addon system hooks
        Utils.InvokeMany(typeof(Pet), this, "LateUpdate_");
    }

    // OnDrawGizmos only happens while the Script is not collapsed
    void OnDrawGizmos() {
        // draw the movement area (around 'start' if game running,
        // or around current position if still editing)
        var startHelp = Application.isPlaying ? owner.petDestination : transform.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(startHelp, returnDistance);

        // draw the follow dist
        Gizmos.color = Color.gray;
        Gizmos.DrawWireSphere(startHelp, followDistance);
    }

    void OnDestroy() {
        // Unity bug: isServer is false when called in host mode. only true when
        // called in dedicated mode. so we need a workaround:
        if (NetworkServer.active) { // isServer
            // keep player's pet item up to date
            SyncToOwnerPetItem();
        }

        // addon system hooks
        Utils.InvokeMany(typeof(Pet), this, "OnDestroy_");
    }

    // always update pets. IsWorthUpdating otherwise only updates if has observers,
    // but pets should still be updated even if they are too far from any observers,
    // so that they continue to run to their owner.
    public override bool IsWorthUpdating() { return true; }

    // finite state machine events /////////////////////////////////////////////
    bool EventDied() {
        return health == 0;
    }

    bool EventDeathTimeElapsed() {
        return state == "DEAD" && Time.time >= deathTimeEnd;
    }

    bool EventTargetDisappeared() {
        return target == null;
    }

    bool EventTargetDied() {
        return target != null && target.health == 0;
    }

    bool EventTargetTooFarToAttack() {
        return target != null &&
               0 <= currentSkill && currentSkill < skills.Count &&
               !CastCheckDistance(skills[currentSkill]);
    }

    bool EventTargetTooFarToFollow() {
        return target != null &&
               Vector3.Distance(owner.petDestination, target.collider.ClosestPointOnBounds(transform.position)) > followDistance;
    }

    bool EventNeedReturnToOwner() {
        return Vector3.Distance(owner.petDestination, transform.position) > returnDistance;
    }

    bool EventNeedTeleportToOwner() {
        return Vector3.Distance(owner.petDestination, transform.position) > teleportDistance;
    }

    bool EventAggro() {
        return target != null && target.health > 0;
    }

    bool EventSkillRequest() {
        return 0 <= currentSkill && currentSkill < skills.Count;
    }

    bool EventSkillFinished() {
        return 0 <= currentSkill && currentSkill < skills.Count &&
               skills[currentSkill].CastTimeRemaining() == 0;
    }

    bool EventMoveEnd() {
        return state == "MOVING" && !IsMoving();
    }

    // finite state machine - server ///////////////////////////////////////////
    [Server]
    string UpdateServer_IDLE() {
        // events sorted by priority (e.g. target doesn't matter if we died)
        if (EventDied()) {
            // we died.
            OnDeath();
            currentSkill = -1; // in case we died while trying to cast
            return "DEAD";
        }
        if (EventTargetDied()) {
            // we had a target before, but it died now. clear it.
            target = null;
            currentSkill = -1;
            return "IDLE";
        }
        if (EventNeedTeleportToOwner()) {
            agent.Warp(owner.petDestination);
            return "IDLE";
        }
        if (EventNeedReturnToOwner()) {
            // return to owner only while IDLE
            target = null;
            currentSkill = -1;
            agent.stoppingDistance = 0;
            agent.destination = owner.petDestination;
            return "MOVING";
        }
        if (EventTargetTooFarToFollow()) {
            // we had a target before, but it's out of follow range now.
            // clear it and go back to start. don't stay here.
            target = null;
            currentSkill = -1;
            agent.stoppingDistance = 0;
            agent.destination = owner.petDestination;
            return "MOVING";
        }
        if (EventTargetTooFarToAttack()) {
            // we had a target before, but it's out of attack range now.
            // follow it. (use collider point(s) to also work with big entities)
            agent.stoppingDistance = CurrentCastRange() * 0.8f;
            agent.destination = target.collider.ClosestPointOnBounds(transform.position);
            return "MOVING";
        }
        if (EventSkillRequest()) {
            // we had a target in attack range before and trying to cast a skill
            // on it. check self (alive, mana, weapon etc.) and target
            var skill = skills[currentSkill];
            if (CastCheckSelf(skill) && CastCheckTarget(skill)) {
                // start casting and set the casting end time
                skill.castTimeEnd = Time.time + skill.castTime;
                skills[currentSkill] = skill;
                return "CASTING";
            } else {
                // invalid target. stop trying to cast.
                target = null;
                currentSkill = -1;
                return "IDLE";
            }
        }
        if (EventAggro()) {
            // target in attack range. try to cast a first skill on it
            if (skills.Count > 0) currentSkill = 0;
            else Debug.LogError(name + " has no skills to attack with.");
            return "IDLE";
        }
        if (EventMoveEnd()) {} // don't care
        if (EventDeathTimeElapsed()) {} // don't care
        if (EventSkillFinished()) {} // don't care
        if (EventTargetDisappeared()) {} // don't care

        return "IDLE"; // nothing interesting happened
    }

    [Server]
    string UpdateServer_MOVING() {
        // events sorted by priority (e.g. target doesn't matter if we died)
        if (EventDied()) {
            // we died.
            OnDeath();
            currentSkill = -1; // in case we died while trying to cast
            agent.ResetPath();
            return "DEAD";
        }
        if (EventMoveEnd()) {
            // we reached our destination.
            return "IDLE";
        }
        if (EventTargetDied()) {
            // we had a target before, but it died now. clear it.
            target = null;
            currentSkill = -1;
            agent.ResetPath();
            return "IDLE";
        }
        if (EventNeedTeleportToOwner()) {
            agent.Warp(owner.petDestination);
            return "IDLE";
        }
        if (EventTargetTooFarToFollow()) {
            // we had a target before, but it's out of follow range now.
            // clear it and go back to start. don't stay here.
            target = null;
            currentSkill = -1;
            agent.stoppingDistance = 0;
            agent.destination = owner.petDestination;
            return "MOVING";
        }
        if (EventTargetTooFarToAttack()) {
            // we had a target before, but it's out of attack range now.
            // follow it. (use collider point(s) to also work with big entities)
            agent.stoppingDistance = CurrentCastRange() * 0.8f;
            agent.destination = target.collider.ClosestPointOnBounds(transform.position);
            return "MOVING";
        }
        if (EventAggro()) {
            // target in attack range. try to cast a first skill on it
            // (we may get a target while randomly wandering around)
            if (skills.Count > 0) currentSkill = 0;
            else Debug.LogError(name + " has no skills to attack with.");
            agent.ResetPath();
            return "IDLE";
        }
        if (EventNeedReturnToOwner()) {} // don't care
        if (EventDeathTimeElapsed()) {} // don't care
        if (EventSkillFinished()) {} // don't care
        if (EventTargetDisappeared()) {} // don't care
        if (EventSkillRequest()) {} // don't care, finish movement first

        return "MOVING"; // nothing interesting happened
    }

    [Server]
    string UpdateServer_CASTING() {
        // keep looking at the target for server & clients (only Y rotation)
        if (target) LookAtY(target.transform.position);

        // events sorted by priority (e.g. target doesn't matter if we died)
        if (EventDied()) {
            // we died.
            OnDeath();
            currentSkill = -1; // in case we died while trying to cast
            return "DEAD";
        }
        if (EventTargetDisappeared()) {
            // target disappeared, stop casting
            currentSkill = -1;
            target = null;
            return "IDLE";
        }
        if (EventTargetDied()) {
            // target died, stop casting
            currentSkill = -1;
            target = null;
            return "IDLE";
        }
        if (EventSkillFinished()) {
            // finished casting. apply the skill on the target.
            CastSkill(skills[currentSkill]);

            // did the target die? then clear it so that the monster doesn't
            // run towards it if the target respawned
            if (target.health == 0) target = null;

            // go back to IDLE
            currentSkill = -1;
            return "IDLE";
        }
        if (EventMoveEnd()) {} // don't care
        if (EventDeathTimeElapsed()) {} // don't care
        if (EventNeedTeleportToOwner()) {} // don't care
        if (EventNeedReturnToOwner()) {} // don't care
        if (EventTargetTooFarToAttack()) {} // don't care, we were close enough when starting to cast
        if (EventTargetTooFarToFollow()) {} // don't care, we were close enough when starting to cast
        if (EventAggro()) {} // don't care, always have aggro while casting
        if (EventSkillRequest()) {} // don't care, that's why we are here

        return "CASTING"; // nothing interesting happened
    }

    [Server]
    string UpdateServer_DEAD() {
        // events sorted by priority (e.g. target doesn't matter if we died)
        if (EventDeathTimeElapsed()) {
            // we were lying around dead for long enough now.
            // hide while respawning, or disappear forever
            NetworkServer.Destroy(gameObject);
            return "DEAD";
        }
        if (EventSkillRequest()) {} // don't care
        if (EventSkillFinished()) {} // don't care
        if (EventMoveEnd()) {} // don't care
        if (EventNeedTeleportToOwner()) {} // don't care
        if (EventNeedReturnToOwner()) {} // don't care
        if (EventTargetDisappeared()) {} // don't care
        if (EventTargetDied()) {} // don't care
        if (EventTargetTooFarToFollow()) {} // don't care
        if (EventTargetTooFarToAttack()) {} // don't care
        if (EventAggro()) {} // don't care
        if (EventDied()) {} // don't care, of course we are dead

        return "DEAD"; // nothing interesting happened
    }

    [Server]
    protected override string UpdateServer() {
        if (state == "IDLE")    return UpdateServer_IDLE();
        if (state == "MOVING")  return UpdateServer_MOVING();
        if (state == "CASTING") return UpdateServer_CASTING();
        if (state == "DEAD")    return UpdateServer_DEAD();
        Debug.LogError("invalid state:" + state);
        return "IDLE";
    }

    // finite state machine - client ///////////////////////////////////////////
    [Client]
    protected override void UpdateClient() {
        if (state == "CASTING") {
            // keep looking at the target for server & clients (only Y rotation)
            if (target) LookAtY(target.transform.position);
        }

        if (ownerNameOverlay != null) {
            if (owner != null) {
                ownerNameOverlay.text = owner.name;
                var player = Utils.ClientLocalPlayer();

                // note: murderer has higher priority (a player can be a murderer and an
                // offender at the same time)
                if (owner.IsMurderer())
                    ownerNameOverlay.color = owner.nameOverlayMurdererColor;
                else if (owner.IsOffender())
                    ownerNameOverlay.color = owner.nameOverlayOffenderColor;
                // member of the same party
                else if (player.InParty() && player.party.GetMemberIndex(owner.name) != -1)
                    ownerNameOverlay.color = owner.nameOverlayPartyColor;
                // otherwise default
                else
                    ownerNameOverlay.color = owner.nameOverlayDefaultColor;
            } else ownerNameOverlay.text = "?";
        }

        // addon system hooks
        Utils.InvokeMany(typeof(Pet), this, "UpdateClient_");
    }

    // combat //////////////////////////////////////////////////////////////////
    // custom DealDamageAt function that also rewards experience if we killed
    // the monster
    [Server]
    public override HashSet<Entity> DealDamageAt(Entity entity, int amount, float aoeRadius=0) {
        // deal damage with the default function. get all entities that were hit
        // in the AoE radius
        var entities = base.DealDamageAt(entity, amount, aoeRadius);
        foreach (var e in entities) {
            // a monster?
            if (e is Monster) {
                // forward to owner to share rewards with everyone
                owner.OnDamageDealtToMonster((Monster)e);
            // a player?
            // (see murder code section comments to understand the system)
            } else if (e is Player) {
                // forward to owner for murderer detection etc.
                owner.OnDamageDealtToPlayer((Player)e);
            // a pet?
            // (see murder code section comments to understand the system)
            } else if (e is Pet) {
                owner.OnDamageDealtToPet((Pet)e);
            }
        }

        // addon system hooks
        Utils.InvokeMany(typeof(Pet), this, "DealDamageAt_", entities, amount);

        return entities; // not really needed anywhere
    }

    // experience //////////////////////////////////////////////////////////////
    public float ExperiencePercent() {
        return (experience != 0 && experienceMax != 0) ? (float)experience / (float)experienceMax : 0;
    }

    // aggro ///////////////////////////////////////////////////////////////////
    // this function is called by entities that attack us
    [ServerCallback]
    public override void OnAggro(Entity entity) {
        // are we alive, and is the entity alive and of correct type?
        if (entity != null && CanAttack(entity)) {
            // no target yet(==self), or closer than current target?
            // => has to be at least 20% closer to be worth it, otherwise we
            //    may end up nervously switching between two targets
            // => we do NOT use Utils.ClosestDistance, because then we often
            //    also end up nervously switching between two animated targets,
            //    since their collides moves with the animation.
            if (target == null) {
                target = entity;
            } else {
                float oldDistance = Vector3.Distance(transform.position, target.transform.position);
                float newDistance = Vector3.Distance(transform.position, entity.transform.position);
                if (newDistance < oldDistance * 0.8) target = entity;
            }

            // addon system hooks
            Utils.InvokeMany(typeof(Pet), this, "OnAggro_", entity);
        }
    }

    // death ///////////////////////////////////////////////////////////////////
    [Server]
    void OnDeath() {
        // set death end time. we set it now to make sure that everything works
        // fine even if a pet isn't updated for a while. so as soon as it's
        // updated again, the death/respawn will happen immediately if current
        // time > end time.
        deathTimeEnd = Time.time + deathTime;

        // stop buffs, clear target
        StopBuffs();
        target = null;

        // keep player's pet item up to date
        SyncToOwnerPetItem();

        // addon system hooks
        Utils.InvokeMany(typeof(Pet), this, "OnDeath_");
    }

    // skills //////////////////////////////////////////////////////////////////
    // monsters always have a weapon
    public override bool HasCastWeapon() { return true; }

    // monsters can only attack players
    public override bool CanAttack(Entity entity) {
        return health > 0 &&
               entity.health > 0 &&
               entity != this &&
               (entity.GetType() == typeof(Monster) ||
                (entity.GetType() == typeof(Player) && entity != owner) ||
                (entity.GetType() == typeof(Pet) && ((Pet)entity).owner != owner));
    }

    // helper function to get the current cast range (if casting anything)
    public float CurrentCastRange() {
        return 0 <= currentSkill && currentSkill < skills.Count ? skills[currentSkill].castRange : 0;
    }
}
