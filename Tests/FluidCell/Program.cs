using System;
using System.Collections.Generic;
using Khemistry;
class Program
{
    static int count;
    static void Check(bool ok, string why) { count++; if (!ok) throw new Exception(why); }
    static void Equal(double actual,double expected,string why) => Check(Math.Abs(actual-expected)<1e-8,why+": "+actual);
    static int Main()
    {
        try
        {
            var cell = new KhemistryFluidCell();
            Check(cell.maxInputRate == -1 && cell.maxOutputRate == -1 && cell.maxInputRateInternal == -1 && cell.maxOutputRateInternal == -1,"defaults");
            Equal(cell.RequestStoredResource("Water",-20),-20,"unlimited input");
            cell.maxOutputRate=2.5;
            Equal(cell.RequestStoredResource("Water",20),2.5,"decimal limit");
            Equal(cell.RequestStoredResource("Water",20),0,"aggregate same tick");
            UnityEngine.Time.fixedTime++;
            Equal(cell.RequestStoredResource("Water",20),2.5,"next tick");
            cell.maxInputRate=1;
            Equal(cell.RequestStoredResource("A",-1),-1,"input independent");
            Equal(cell.RequestStoredResource("B",-1),0,"shared across resources");
            cell.maxOutputRate=0; Equal(cell.RequestStoredResource("Water",1),0,"zero blocks");
            cell.maxOutputRate=-2; Equal(cell.RequestStoredResource("Water",5),5,"all negative unlimited");
            var k=new KhemistryKerbal(); var s=new StoredPart();
            k.prefab.maxInputRate=2;
            var e=new KhemistryResourceNetwork.Endpoint { amount=20 };
            k.Start(s,e,true,7);
            Equal(k.Amount(s),2,"first transfer step");
            Check(k.Jobs==1,"queued");
            k.Tick(); Equal(k.Amount(s),4,"continued");
            k.Tick(); k.Tick(); Equal(k.Amount(s),7,"final partial");
            Equal(e.amount,13,"conservation"); Check(k.Jobs==0,"completed");
            k.prefab.maxOutputRate=1; k.Start(s,e,false,5);
            Equal(k.Amount(s),6,"output independent");
            k.inRange=false; k.Tick(); Check(k.Jobs==0,"range cancels"); Equal(k.Amount(s),6,"no remote transfer");
            k.inRange=true; k.Start(s,e,false,3); k.CancelCellTransfers(); k.Tick(); Check(k.Jobs==0,"cancel");
            var transfers=new List<KhemistryResourceNetwork.Transfer>();
            k.prefab.maxOutputRateInternal=0.5; k.prefab.maxInputRateInternal=0;
            Equal(k.RequestInventoryProcessorResource(s,"Water",2,false,transfers),0.5,"internal limit");
            Equal(k.RequestInventoryProcessorResource(s,"Water",2,false),0,"internal shared");
            var before=k.Amount(s); KhemistryResourceNetwork.Rollback(transfers);
            Equal(k.Amount(s),before+0.5,"refund bypasses blocked input");
            Equal(k.RequestInventoryProcessorResource(s,"Water",2,false),0.5,"refund releases original budget");
            var s2=new StoredPart();
            Equal(k.RequestInventoryProcessorResource(s2,"Water",-2,false),0,"internal zero");
            k.prefab.maxInputRateInternal=1.25;
            Equal(k.RequestInventoryProcessorResource(s2,"Water",-2,false),-1.25,"internal decimal");
            Equal(k.RequestInventoryProcessorResource(s2,"A",-2,false),0,"internal aggregate resources");
            Equal(k.RequestInventoryProcessorResource(s,"A",-2,false),-1.25,"separate cell budget");
            k.Tick();
            var target=new KhemistryResourceNetwork.Endpoint { acceptance=0.25 };
            k.Start(s,target,false,1);
            Equal(target.amount,0.25,"partial endpoint acceptance");
            k.Tick(); k.Tick(); k.Tick(); Equal(target.amount,1,"retry remainder");
            Check(k.Jobs==0,"partial transfer complete");
            var stalled=new KhemistryResourceNetwork.Endpoint { amount=0 };
            k.Start(s2,stalled,true,1); k.Tick(); Check(k.Jobs==1,"empty target pauses");
            stalled.amount=1; k.Tick(); Check(k.Jobs==0,"target refill resumes");
            k.prefab.maxInputRate=0; k.Start(s2,stalled,true,1); Check(k.Jobs==0,"zero-rate transfer rejected");
            k.prefab.maxInputRate=-1; var suitSource = new KhemistryResourceNetwork.Endpoint { amount=3, acceptance=0.5 };
            k.Start(null,suitSource,true,2); k.Tick(); k.Tick(); k.Tick();
            Equal(suitSource.amount,1,"suit transfer retries limited endpoint"); Check(k.Jobs==0,"suit transfer completes");
            var budget = new FluidCellRateBudget();
            Equal(budget.Available(0,4,100,0.25),1,"simulation timestep scaling");
            budget.Record(0,0.75,100);
            Equal(budget.Available(0,4,100,0.25),0.25,"aggregate fractional allowance");
            Equal(budget.Available(1,4,100,0.25),1,"direction independence");
            Equal(budget.Available(2,4,100,0.25),1,"internal external independence");
            Equal(budget.Available(0,4,101,0.25),1,"no idle accumulation");
            Console.WriteLine("Passed "+count+" fluid-cell assertions."); return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
