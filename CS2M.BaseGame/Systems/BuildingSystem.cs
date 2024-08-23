using Game;
using Game.Buildings;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace CS2M.BaseGame.Systems
{
    public partial class BuildingSystem : GameSystemBase
    {
        private EntityQuery _query;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Create a query that returns residential buildings with an electricity consumer component.
            // We don't want to bother with Temp or Deleted entities,
            // so we exclude entities that have those components from our query
            _query = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.Exclude<Temp>());

            // Lets the system only run if there is at least one match to our query.
            RequireForUpdate(_query);
        }

        protected override void OnUpdate()
        {
            
        }
    }
}
