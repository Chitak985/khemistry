using System;
using Khemistry;

static partial class Program
{
    static ConfigNode Passive(string condition = null, string failure = "PAUSE",
        double amount = 2, double period = 1, bool ignore = false, string resource = "B")
    {
        var module = new ConfigNode();
        var input = module.AddNode("PINPUT_RESOURCE");
        input.AddValue("name", resource);
        input.AddValue("amount", amount);
        input.AddValue("period", period);
        input.AddValue("powerfail", failure);
        input.AddValue("ignorePowerfail", ignore);
        if (condition != null) input.AddValue("condition", condition);
        return module;
    }

    static void PassiveTests()
    {
        var (part, storage, _) = Setup();
        var supply = new PartResource { resourceName = "B", amount = 100, maxAmount = 100 };
        part.Resources.Add(supply);
        Equal(1, storage.ConfigurePassive(Passive()) ? 1 : 0, "default passive config");
        storage.TickPassive();
        Equal(98, supply.amount, "always consumes when on and empty");
        storage.state = KShared.ChargablePartState.Off;
        storage.TickPassive(10);
        Equal(98, supply.amount, "always disabled when off");
        storage.state = KShared.ChargablePartState.Charging;
        storage.TickPassive(10);
        Equal(98, supply.amount, "always disabled when charging");
        storage.state = KShared.ChargablePartState.On;
        storage.TickPassive(10, 0.5);
        Equal(98, supply.amount, "only powered fraction counts at charging transition");
        storage.TickPassive(0.5);
        Equal(96, supply.amount, "period remainder retained");

        storage.ConfigurePassive(Passive("moreThan0"));
        storage.TickPassive(10);
        Equal(96, supply.amount, "moreThan0 ignores empty on storage");
        storage.PutResource("A", 4);
        storage.state = KShared.ChargablePartState.Off;
        storage.TickPassive(2);
        Equal(92, supply.amount, "moreThan0 consumes while off and filled");
        storage.state = KShared.ChargablePartState.Charging;
        storage.TickPassive();
        Equal(90, supply.amount, "moreThan0 consumes while charging and filled");
        Equal(4, storage.GetStoredAmount("A"), "off and charging do not void cargo");

        storage.state = KShared.ChargablePartState.On;
        storage.ConfigurePassive(Passive());
        supply.amount = 1;
        storage.TickPassive();
        Equal(1, storage.Paused ? 1 : 0, "shortage pauses");
        Equal(1, supply.amount, "partial request rolled back");
        Equal(0, storage.TakeResource("A", 1), "pause blocks direct withdrawal");
        Equal(0, storage.PutResource("A", 1), "pause blocks filling");
        storage.TickPassive(100);
        Equal(1, supply.amount, "waiting interval does not consume partial resources");
        supply.amount = 2;
        storage.TickPassive();
        Equal(0, supply.amount, "retry consumes only owed interval");
        Equal(0, storage.Paused ? 1 : 0, "pause recovers automatically");
        Equal(1, storage.TakeResource("A", 1), "transfer access restored");

        // Save a paused transaction and reload it before paying its debt.
        supply.amount = 0;
        storage.TickPassive();
        var saved = new ConfigNode();
        storage.OnSave(saved);
        var beforeStart = new ConfigNode();
        storage.OnLoad(saved);
        storage.OnSave(beforeStart);
        storage.OnLoad(beforeStart);
        storage.Ready();
        storage.ConfigurePassive(Passive());
        Equal(1, storage.Paused ? 1 : 0, "paused save restored, including save before start");
        supply.amount = 2;
        storage.TickPassive();
        Equal(0, supply.amount, "saved interval not lost or duplicated");

        // Paid consumption is refunded by STOP, not by VOID. Stored cargo is never voided.
        storage.ConfigurePassive(Passive(failure: "STOP"));
        supply.amount = 4;
        storage.TickPassive();
        storage.TickPassive();
        storage.TickPassive();
        Equal(4, supply.amount + storage.GetStoredAmount("B"), "STOP refunds paid upkeep through resource network");
        Equal((int)KShared.ChargablePartState.Off, (int)storage.state, "STOP turns off");
        Equal(3, storage.GetStoredAmount("A"), "STOP preserves cargo");
        storage.state = KShared.ChargablePartState.On;
        storage.ConfigurePassive(Passive(failure: "VOID"));
        storage.TickPassive();
        storage.TickPassive();
        storage.TickPassive();
        Equal(0, supply.amount, "VOID does not refund upkeep");
        Equal(3, storage.GetStoredAmount("A"), "VOID preserves cargo");

        storage.state = KShared.ChargablePartState.On;
        storage.ConfigurePassive(Passive(failure: "MAINT"));
        storage.TickPassive();
        Equal(1, storage.Maintenance ? 1 : 0, "MAINT requires repair");
        Equal(0, storage.TransfersEnabled ? 1 : 0, "MAINT blocks transfers");
        saved = new ConfigNode();
        storage.OnSave(saved);
        storage.OnLoad(saved); storage.Ready(); storage.ConfigurePassive(Passive(failure: "MAINT"));
        Equal(1, storage.Maintenance ? 1 : 0, "maintenance lock persisted");
        FlightGlobals.ActiveVessel = new Vessel();
        FlightGlobals.ActiveVessel.crew.Add(new ProtoCrewMember { trait = "Pilot" });
        storage.PerformStorageMaintenance();
        Equal(1, storage.Maintenance ? 1 : 0, "pilot cannot repair");
        FlightGlobals.ActiveVessel.crew[0].trait = "Engineer";
        storage.PerformStorageMaintenance();
        Equal(0, storage.Maintenance ? 1 : 0, "engineer repairs");
        storage.state = KShared.ChargablePartState.On;
        storage.ConfigurePassive(Passive(failure: "EXPLODE,5,200"));
        storage.TickPassive();
        Equal(1, KShared.Explosions, "EXPLODE invokes explosion");
        storage.state = KShared.ChargablePartState.On;
        storage.ConfigurePassive(Passive(ignore: true));
        supply.amount = 5;
        storage.TickPassive(3);
        Equal(1, supply.amount, "ignorePowerfail keeps whole occurrences only");
        Equal(0, storage.Paused ? 1 : 0, "ignored shortage does not pause");
        storage.TickPassive(0.1);
        Equal(1, supply.amount, "ignored missing occurrences not owed");

        // Two inputs must roll back together when a later one fails.
        (part, storage, _) = Setup();
        var config = Passive(resource: "A");
        config.AddNode(Passive(resource: "B").GetNodes("PINPUT_RESOURCE")[0]);
        storage.ConfigurePassive(config);
        storage.PutResource("A", 4);
        storage.TickPassive();
        Equal(4, storage.GetStoredAmount("A"), "later failure refunds earlier internal input");
        part.Resources.Add(new PartResource { resourceName = "B", amount = 2, maxAmount = 2 });
        storage.TickPassive();
        Equal(2, storage.GetStoredAmount("A"), "paused storage can supply own upkeep on retry");
        Equal(0, storage.Paused ? 1 : 0, "multi-input retry unpauses");

        // Config reorder must not swap saved timers, and inactive conditions must freeze clocks.
        config = Passive(period: 2, resource: "A");
        config.AddNode(Passive(period: 4, resource: "B").GetNodes("PINPUT_RESOURCE")[0]);
        storage.ConfigurePassive(config);
        storage.TickPassive(1);
        saved = new ConfigNode(); storage.OnSave(saved);
        storage.OnLoad(saved); storage.Ready();
        var reversed = Passive(period: 4, resource: "B");
        reversed.AddNode(Passive(period: 2, resource: "A").GetNodes("PINPUT_RESOURCE")[0]);
        storage.ConfigurePassive(reversed);
        storage.TickPassive(1);
        Equal(0, storage.GetStoredAmount("A"), "timer restored by identity after reorder");
        Equal(0, storage.Paused ? 1 : 0, "later-period input not incorrectly due");

        // Refunds remain conserved if no destination has space at the time of STOP.
        (part, storage, _) = Setup();
        supply = new PartResource { resourceName = "B", amount = 2, maxAmount = 0 };
        part.Resources.Add(supply);
        storage.PutResource("A", 10);
        config = Passive(failure: "STOP");
        storage.ConfigurePassive(config);
        storage.TickPassive();
        saved = new ConfigNode(); storage.OnSave(saved);
        storage.OnLoad(saved); storage.Ready(); storage.ConfigurePassive(config);
        storage.TickPassive();
        saved = new ConfigNode(); storage.OnSave(saved);
        Equal(1, saved.GetNodes("STORAGE_PASSIVE_STATE")[0].GetNodes("REFUND").Length,
            "STOP refund without capacity is saved, including upkeep paid before reload");
        storage.OnLoad(saved); storage.Ready(); storage.ConfigurePassive(config);
        supply.maxAmount = 2;
        storage.TickPassive();
        Equal(2, supply.amount, "pending refund retries after reload while off");
        storage.TickPassive();
        Equal(2, supply.amount, "pending refund cannot duplicate resources");

        Equal(0, storage.ConfigurePassive(Passive(condition: "never")) ? 1 : 0, "invalid condition rejected");
        Equal(0, storage.ConfigurePassive(Passive(amount: -1)) ? 1 : 0, "negative amount rejected");
        Equal(0, storage.ConfigurePassive(Passive(period: 0)) ? 1 : 0, "zero period rejected");
        Equal(0, storage.ConfigurePassive(Passive(resource: "NoSuchResource")) ? 1 : 0, "unknown resource rejected");
        config = Passive();
        config.GetNodes("PINPUT_RESOURCE")[0].AddValue("ignorePowerfail", "maybe");
        Equal(0, storage.ConfigurePassive(config) ? 1 : 0, "invalid boolean rejected");
        config = Passive(failure: "notAnAction");
        Equal(1, storage.ConfigurePassive(config) ? 1 : 0, "invalid failure falls back to PAUSE like ISRU");
    }
}
