using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Utils;

namespace TSUT.H2Real
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_HydrogenEngine), true)]
    class HyderogenGenerators : MyGameLogicComponent
    {
        private IMyPowerProducer block;

        public override void Init(MyComponentDefinitionBase definition)
        {
            base.Init(definition);
            block = (IMyPowerProducer)Entity;
            var sink = block.Components.Get<MyResourceSinkComponent>();
            var hydrogenId = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Hydrogen");
            var maxH2Consumption = sink.MaxRequiredInputByType(hydrogenId);
            var oxygenId = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Oxygen");
            MyResourceSinkInfo sinkInfo = new MyResourceSinkInfo();
            sinkInfo.ResourceTypeId = oxygenId;
            sinkInfo.MaxRequiredInput = maxH2Consumption / 2;
            sinkInfo.RequiredInputFunc = GetCurrentO2ConsumptionInt;
            sink.AddType(ref sinkInfo);
        }

        float GetCurrentH2Consumption()
        {
            var sink = block?.Components.Get<MyResourceSinkComponent>();
            if (sink == null)
                return 0f;

            var hydrogenId = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Hydrogen");
            var currentConsumption = sink.CurrentInputByType(hydrogenId);

            return currentConsumption; // L/s
        }

        float GetCurrentO2ConsumptionInt()
        {
            var result = GetCurrentH2Consumption() * 0.5f;
            MyLog.Default.WriteLine($"[H2Real] Requested consumption for {block.DisplayNameText}: {result}");
            return result;
        }
    }
}