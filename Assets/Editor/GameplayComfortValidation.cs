using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Guandan.Game;
using Guandan.UI;
using Guandan.XR;
using Guandan.Scene;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Guandan.Editor
{
    // Editor-only scenario fixtures exercise the actual buttons, view transitions and
    // throw coroutine. Match result fixtures isolate UI states; rule smoke tests run separately.
    public static class GameplayComfortValidation
    {
        private static int stage;
        private static double stageTime;
        private static SocialProp tomato;
        private static Vector3 tableEye;
        private static Transform northHead;
        private static Quaternion northHeadRotation;
        private static Quaternion winnerStartRotation;
        private static Vector3 winnerHopPosition;
        public static void Run()
        {
            TombTitleValidation.ImportCover();
            GuandanProjectSetup.VerifyProject();
            GuandanRuleSmokeTests.Run();
            EditorSceneManager.OpenScene(GuandanProjectSetup.ScenePath);
            SessionState.SetBool("ComfortQA", true);
            EditorApplication.isPlaying = true;
        }
        [InitializeOnLoadMethod] private static void Resume()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("ComfortQA", false))
                { stage = 0; stageTime = EditorApplication.timeSinceStartup; EditorApplication.update += Tick; }
            };
        }
        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup - stageTime < (stage == 4 ? 0.52 : 1.0)) return;
            try
            {
                var director = UnityEngine.Object.FindAnyObjectByType<GameDirector>();
                var ui = UnityEngine.Object.FindAnyObjectByType<ScreenGameUi>();
                var rig = UnityEngine.Object.FindAnyObjectByType<GuandanXRBootstrap>();
                switch (stage)
                {
                    case 0:
                        tableEye = Camera.main.transform.position;
                        Require(Mathf.Abs(tableEye.y - 3.20f) < 0.01f, "Lower initial eye");
                        foreach (var seat in new[] { PlayerSeat.North, PlayerSeat.East, PlayerSeat.West })
                            foreach (var legal in new[] { 0, 1, 30, 500 })
                                foreach (var variation in new[] { 0f, 0.5f, 1f })
                                {
                                    var duration=GameDirector.AiThinkDuration(director.GetProfile(seat),legal,true,5,variation);
                                    Require(duration>=0.45f && duration<=2f,"Bounded human-paced AI think time");
                                }
                        Capture("01-lottery"); director.ChooseLottery(0); break;
                    case 1:
                        if (!director.LotteryChosen || director.IsPresentationLocked) return;
                        director.StopAllCoroutines(); director.enabled = false;
                        Set(director,"aiRoutine",null); Set(director,"dealRoutine",null);
                        var deck=Deck.CreateDoubleDeck();
                        var fixtureCards=deck.Where(c=>c.Rank==5).Take(3).Concat(deck.Where(c=>c.Rank==12).Take(2)).ToArray();
                        var fixturePattern=GuandanRuleEngine.FindPatternsForSelection(fixtureCards,director.Match.LevelRank,null).First();
                        var bombCards=deck.Where(c=>c.Rank==8).Take(8).ToArray();
                        var bombPattern=GuandanRuleEngine.FindPatternsForSelection(bombCards,director.Match.LevelRank,null).First();
                        var patterns=(Dictionary<PlayerSeat,PlayPattern>)typeof(GameDirector).GetField("seatPlayPatterns",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(director);
                        patterns[PlayerSeat.North]=fixturePattern; patterns[PlayerSeat.West]=fixturePattern; patterns[PlayerSeat.East]=bombPattern;
                        var wild=deck.First(c=>c.IsWildcard(director.Match.LevelRank));
                        var wildCards=deck.Where(c=>c.Rank==8).Take(2).Concat(deck.Where(c=>c.Rank==3).Take(2)).Append(wild).ToArray();
                        patterns[PlayerSeat.West]=GuandanRuleEngine.FindPatternsForSelection(wildCards,director.Match.LevelRank,null)
                            .First(p=>p.Kind==PlayKind.FullHouse && p.MainRank==8 && p.Wildcards.Any(w=>w.CardId==wild.Id && w.RepresentedRank==8));
                        Require(director.TryGetSeatWildcard(PlayerSeat.West,wild.Id,out var actualUse) && actualUse.RepresentedRank==8,"Actual wildcard meaning retained");
                        ui.SetSpatialSeatPlaques(true); Set(ui,"spatialXrMode",true); ui.Refresh();
                        var orderedFullHouse=director.GetSeatPlayCards(PlayerSeat.North).Select(c=>c.Rank).ToArray();
                        Require(orderedFullHouse.SequenceEqual(new[]{5,5,5,12,12}),"Full house keeps equal visible ranks together");
                        var cardLabel=typeof(ScreenGameUi).GetMethod("CardLabel",BindingFlags.Static|BindingFlags.NonPublic);
                        var smallJoker=deck.First(c=>c.IsSmallJoker); var bigJoker=deck.First(c=>c.IsBigJoker);
                        Require((string)cardLabel.Invoke(null,new object[]{smallJoker,director.Match.LevelRank})=="小王","Small joker label has no duplicate 王");
                        Require((string)cardLabel.Invoke(null,new object[]{bigJoker,director.Match.LevelRank})=="大王","Big joker label has no duplicate 王");
                        break;
                    case 2:
                        var plaques=UnityEngine.Object.FindObjectsByType<WorldSeatStatusUi>(FindObjectsSortMode.None);
                        Require(plaques.Length==3 && plaques.All(p=>p.GetComponentInChildren<Canvas>()!=null),"Three live seat plaques");
                        foreach(var plaque in plaques)
                        {
                            var seat=(PlayerSeat)typeof(WorldSeatStatusUi).GetField("seat",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(plaque);
                            if(!ui.TryGetPlayedCardViewportBounds(seat,Camera.main,out var cardsBounds)) continue;
                            var rect=plaque.GetComponentInChildren<Canvas>().GetComponent<RectTransform>();
                            var corners=new Vector3[4]; rect.GetWorldCorners(corners);
                            var points=corners.Select(Camera.main.WorldToViewportPoint).ToArray();
                            var box=Rect.MinMaxRect(points.Min(v=>v.x),points.Min(v=>v.y),points.Max(v=>v.x),points.Max(v=>v.y));
                            Require(!box.Overlaps(cardsBounds),seat+" plaque clears played cards");
                            var rows=plaque.GetComponentsInChildren<RectTransform>(true).Where(r=>r.name.StartsWith("Mini_")).Select(r=>Mathf.Round(r.anchoredPosition.y)).Distinct().Count();
                            Require(rows<=1,seat+" played cards stay on one row");
                            foreach(var card in plaque.GetComponentsInChildren<RectTransform>(true).Where(r=>r.name.StartsWith("Mini_")))
                            {
                                var rank=card.Find("Rank")?.GetComponent<Text>(); var suit=card.Find("Suit")?.GetComponent<Text>();
                                Require(rank!=null && rank.fontStyle==FontStyle.Bold,seat+" played rank is bold");
                                Require(suit==null || suit.fontStyle==FontStyle.Normal,seat+" played suit remains normal weight");
                            }
                        }
                        Capture("02-table");
                        Require(plaques.SelectMany(p=>p.GetComponentsInChildren<Text>()).Any(t=>t.name=="WildcardMeaning" && t.text=="配\n8"),"Played wildcard badge states represented rank");
                        Require(plaques.SelectMany(p=>p.GetComponentsInChildren<Text>()).Any(t=>t.name=="WildcardExplanationText" && t.text=="配作 8" && t.fontSize>=68),"Readable wildcard meaning beneath single-row cards");
                        // The two status labels now flank the action buttons, below all hand cards.
                        var hand = FindRect(ui,"HandRoot");
                        var cardTop = hand.anchoredPosition.y + 67f + 69f + 18f;
                        var passRect=FindRect(ui,"Pass"); var playRect=FindRect(ui,"Play");
                        var handInfo=FindRect(ui,"HandInfo"); var selection=FindRect(ui,"Selection");
                        Require(handInfo.anchoredPosition.y==passRect.anchoredPosition.y && selection.anchoredPosition.y==playRect.anchoredPosition.y,"Hand status beside action buttons");
                        Require(handInfo.anchoredPosition.x+handInfo.rect.width/2<passRect.anchoredPosition.x-passRect.rect.width/2,"Hand count clears pass");
                        Require(selection.anchoredPosition.x-selection.rect.width/2>playRect.anchoredPosition.x+playRect.rect.width/2,"Selection clears play");
                        var turnRect=FindRect(ui,"TurnSouth"); Require(turnRect.anchoredPosition.y-turnRect.rect.height/2>cardTop+6,"Turn status clears hand");
                        var clamp=typeof(GuandanXRBootstrap).GetMethod("ConstrainWalkOffset",BindingFlags.Static|BindingFlags.NonPublic);
                        var limit=(Vector3)clamp.Invoke(null,new object[]{new Vector3(9,0,9)});
                        Require(Mathf.Abs(limit.x-0.6f)<0.001f && Mathf.Abs(limit.z-0.35f)<0.001f,"Walk boundary");
                        Set(rig,"desktopPosition",tableEye+limit); Call(rig,"ApplyDesktopCamera");
                        Capture("03-table-moved"); rig.RecenterToDesignStart();
                        tomato=UnityEngine.Object.FindObjectsByType<SocialProp>(FindObjectsSortMode.None).First(p=>p.PropType==SocialPropType.Tomato);
                        var north=director.GetAvatarTransform(PlayerSeat.North);
                        var animator=north.GetComponentsInChildren<Animator>().FirstOrDefault(a=>a.enabled);
                        northHead=animator!=null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : north.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name.EndsWith("Head",StringComparison.OrdinalIgnoreCase));
                        Require(northHead!=null,"North head bone exists for regression check");
                        northHeadRotation=northHead.localRotation;
                        foreach(var animation in north.GetComponentsInChildren<Animator>()) animation.enabled=false;
                        tomato.Interact(PointerSource.Desktop); break;
                    case 3:
                        Require(tomato.ReadyToThrow,"Tomato lifted");
                        var avatar=director.GetAvatarTransform(PlayerSeat.North);
                        Require(avatar!=null,"North avatar");
                        var target=avatar.GetComponent<Guandan.Game.AvatarTarget>();
                        if(target==null) target=avatar.GetComponentInParent<Guandan.Game.AvatarTarget>();
                        Require(target!=null,"Avatar hit target");
                        target.Interact(PointerSource.Desktop);
                        Require(GameObject.Find("Reaction · Tomato body impact")==null,"No splash before arrival");
                        break;
                    case 4:
                        var splash=GameObject.Find("Reaction · Tomato body impact");
                        if(splash==null && EditorApplication.timeSinceStartup-stageTime<1.2) return;
                        Require(splash!=null,"Body splash at impact");
                        Require(splash.transform.position.y>director.GetAvatarTransform(PlayerSeat.North).position.y+0.4f,"Splat above floor");
                        if(northHead!=null) Require(Quaternion.Angle(northHeadRotation,northHead.localRotation)<0.01f,"Tomato does not modify head bone");
                        Capture("04-tomato-hit"); break;
                    case 5:
                        if(EditorApplication.timeSinceStartup-stageTime<3.3) return;
                        Require(GameObject.Find("Reaction · Tomato body impact")==null,"Effect cleanup");
                        Require(SocialProp.Held==null,"Throw releases held prop");
                        if(northHead!=null) Require(Quaternion.Angle(northHeadRotation,northHead.localRotation)<0.01f,"No cumulative head deformation after tomato");
                        SetResult(director,0); director.EnterTreasureView(); ui.Refresh(); break;
                    case 6:
                        Require(director.TreasureViewActive,"Enter treasure");
                        var glow=GameObject.Find("TreasureGlow · 宝箱内部金光");
                        Require(glow!=null && glow.transform.position.x>6f,"Glow binds the screenshot's open right chest");
                        Require(glow.GetComponent<Light>().intensity>1f,"Ambient gold light before winning");
                        Require(glow.GetComponent<ParticleSystem>().particleCount>0,"Ambient treasure sparkles visible");
                        Require(glow.GetComponent<ParticleSystem>().emission.rateOverTime.constant>=38,"Treasure sparkle strengthened");
                        Require(glow.GetComponent<ParticleSystemRenderer>().sharedMaterial.shader.name=="Guandan/TreasureSparkle","Packaged sparkle shader");
                        Require(FindRect(ui,"Risky").GetComponentInChildren<Text>().fontSize>=28,"Route button text not capped at 20");
                        var savedPosition=Camera.main.transform.position; var savedRotation=Camera.main.transform.rotation;
                        Camera.main.transform.position=glow.transform.position+new Vector3(-1.8f,1.0f,-3.5f);
                        Camera.main.transform.LookAt(glow.transform.position+Vector3.up*0.2f);
                        Capture("09-chest-gold-closeup");
                        Camera.main.transform.SetPositionAndRotation(savedPosition,savedRotation);
                        Require(FindRect(ui,"Steady").rect.width>=430f,"Treasure choice panels enlarged");
                        Require(FindRect(ui,"RouteTitle").GetComponent<Text>().fontSize>=42,"Treasure title enlarged");
                        Require(FindRect(ui,"BlueRoute").GetComponentsInChildren<Image>(true).Any(i=>i.name.StartsWith("Step_") && i.rectTransform.rect.width>=52f),"Treasure route cells enlarged");
                        AssertReachable(ui,"Steady"); Capture("05-treasure-choice");
                        Click(ui,"Steady"); ui.Refresh(); break;
                    case 7:
                        Require(director.RaceResolved,"Route resolved");
                        AssertReachable(ui,"Continue"); Capture("06-treasure-return");
                        var eye=Camera.main.transform.position;
                        rig.RecenterToDesignStart(); Require(Vector3.Distance(eye,Camera.main.transform.position)<0.01f,"Recenter stays in treasure");
                        Click(ui,"Continue"); break;
                    case 8:
                        Require(!director.TreasureViewActive && !director.RaceOpen,"Return clears treasure state");
                        Require(Vector3.Distance(Camera.main.transform.position,tableEye)<0.01f,"Camera returns to table");
                        Require(ui.Canvas.transform.position.z<0,"UI returns to table");
                        Require(director.Match.HandNumber==2,"Next hand starts");
                        Capture("07-returned-table");
                        director.StopAllCoroutines(); SetResult(director,1); director.EnterTreasureView();
                        director.StartCoroutine((System.Collections.IEnumerator)typeof(GameDirector).GetMethod("RunAiRoute",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(director,null));
                        ui.Refresh(); break;
                    case 9:
                        if(EditorApplication.timeSinceStartup-stageTime<1.25) return;
                        Require(director.RaceResolved,"Opponent route result"); AssertReachable(ui,"Continue"); Click(ui,"Continue");
                        director.StopAllCoroutines(); SetResult(director,0);
                        var presenter=UnityEngine.Object.FindFirstObjectByType<TreasureScenePresenter>();
                        var winner=((Transform[])typeof(TreasureScenePresenter).GetField("pieces",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(presenter))[0];
                        winnerStartRotation=winner.rotation;
                        while (director.Race.GetPosition(0)<11) director.Race.Move(0,1,TreasureRoute.Steady,0.5); director.EnterTreasureView();
                        director.HandleAction(GameAction.SteadyRoute); ui.Refresh(); break;
                    case 10:
                        if(!director.MatchResultReady && EditorApplication.timeSinceStartup-stageTime<3) return;
                        Require(director.Race.IsComplete,"Treasure completed"); AssertReachable(ui,"Restart"); Capture("08-treasure-complete");
                        Require(FindRect(ui,"MatchResultTitle").GetComponent<Text>().text=="恭喜获胜","Victory panel copy");
                        Require(FindRect(ui,"MatchResultPanel").GetComponent<RawImage>().texture!=null,"Nano Banana victory texture");
                        var resultPanel=FindRect(ui,"MatchResultPanel");
                        var resultTitle=FindRect(ui,"MatchResultTitle"); var resultDetail=FindRect(ui,"MatchResultDetail");
                        var resultHalf=resultPanel.rect.height*0.5f;
                        var resultTopMargin=resultHalf-(resultTitle.anchoredPosition.y+resultTitle.rect.height*0.5f);
                        var resultBottomMargin=resultHalf+resultDetail.anchoredPosition.y-resultDetail.rect.height*0.5f;
                        Require(Mathf.Abs(resultTopMargin-resultBottomMargin)<1f,"Result text has equal top and bottom margins");
                        Require(resultTitle.anchoredPosition.y>38f && resultDetail.anchoredPosition.y>-90f,"Result text group moved upward");
                        var celebration=UnityEngine.Object.FindFirstObjectByType<TreasureCelebration>();
                        Require(celebration.IsPlaying,"Victory confetti active");
                        Require(GameObject.Find("ConfettiCannonLeft")!=null && GameObject.Find("ConfettiCannonRight")!=null,"Two celebration cannons");
                        var winPiece=GetPiece(0);
                        Require(Quaternion.Angle(winPiece.rotation,winnerStartRotation)>35,"Winner turns toward viewer");
                        winnerHopPosition=winPiece.position;
                        break;
                    case 11:
                        Require(Vector3.Distance(GetPiece(0).position,winnerHopPosition)>0.025f,"Winning piece hops left/right");
                        Capture("10-victory-hopping");
                        var liveCamera=Camera.main; var previousEye=liveCamera.transform.position; var previousLook=liveCamera.transform.rotation;
                        var champion=GetPiece(0); var renderers=champion.GetComponentsInChildren<Renderer>();
                        var championBounds=renderers[0].bounds; foreach(var renderer in renderers) championBounds.Encapsulate(renderer.bounds);
                        var championViewport=liveCamera.WorldToViewportPoint(championBounds.center);
                        Require(championViewport.x>0.08f && championViewport.x<0.91f,"Champion inside default final composition");
                        var closer=(previousEye-championBounds.center).normalized;
                        liveCamera.transform.position=championBounds.center+closer*3.6f;
                        liveCamera.transform.LookAt(championBounds.center);
                        Capture("12-winner-facing-closeup");
                        liveCamera.transform.SetPositionAndRotation(previousEye,previousLook);
                        Click(ui,"Restart"); break;
                    case 12:
                        Require(!director.LotteryChosen && !director.TreasureViewActive,"Restart returns to lottery");
                        Require(!UnityEngine.Object.FindFirstObjectByType<TreasureCelebration>().IsPlaying,"Restart clears confetti");
                        Require(Quaternion.Angle(GetPiece(0).rotation,winnerStartRotation)<0.01f,"Restart restores piece orientation");
                        Require(GameObject.Find("TreasureGlow · 宝箱内部金光").GetComponent<ParticleSystem>().isPlaying,"Restart restores ambient treasure sparkle");
                        Require(Vector3.Distance(Camera.main.transform.position,tableEye)<0.01f,"Restart camera returns");
                        Set(director,"lotteryChosen",true); director.StopAllCoroutines(); SetResult(director,1);
                        while(director.Race.GetPosition(1)<11) director.Race.Move(1,1,TreasureRoute.Steady,0.5);
                        director.EnterTreasureView(); director.HandleAction(GameAction.SteadyRoute); break;
                    case 13:
                        if(!director.MatchResultReady && EditorApplication.timeSinceStartup-stageTime<3) return;
                        Require(director.MatchResultReady,"Defeat presentation completes");
                        Require(FindRect(ui,"MatchResultTitle").GetComponent<Text>().text=="惜败此局","Defeat panel copy");
                        Require(FindRect(ui,"MatchResultPanel").GetComponent<RawImage>().texture.name=="ResultDefeat","Distinct defeat artwork");
                        Require(!UnityEngine.Object.FindFirstObjectByType<TreasureCelebration>().IsPlaying,"No congratulatory confetti on player defeat");
                        AssertReachable(ui,"Restart"); Capture("11-defeat"); Click(ui,"Restart"); break;
                    case 14:
                        Require(!director.TreasureViewActive && !FindRect(ui,"MatchResultPanel").gameObject.activeInHierarchy,"Defeat restart clears result");
                        Debug.Log("[ComfortQA] PASS: lower eye, hand/played-card plaque clearance, walk bounds, impact timing/cleanup and unchanged head bone, both teams treasure/return, recenter and match restart.");
                        Finish(0); return;
                }
                stage++; stageTime=EditorApplication.timeSinceStartup;
            }
            catch(Exception error) { Debug.LogException(error); Finish(1); }
        }
        private static void SetResult(GameDirector director,int team)
        {
            Set(director,"raceOpen",true); Set(director,"raceResolved",false); Set(director,"presentationLocked",false);
            typeof(GuandanMatchEngine).GetProperty("Phase").SetValue(director.Match,MatchPhase.HandComplete);
            typeof(GuandanMatchEngine).GetProperty("LastHandResult").SetValue(director.Match,new HandResult{WinningTeam=team,BaseSteps=1,Placements=team==0?new[]{PlayerSeat.South,PlayerSeat.East,PlayerSeat.North,PlayerSeat.West}:new[]{PlayerSeat.East,PlayerSeat.South,PlayerSeat.West,PlayerSeat.North}});
        }
        private static RectTransform FindRect(ScreenGameUi ui,string name)=>ui.Canvas.GetComponentsInChildren<RectTransform>(true).First(t=>t.name==name);
        private static Transform GetPiece(int team)=>( (Transform[])typeof(TreasureScenePresenter).GetField("pieces",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(UnityEngine.Object.FindFirstObjectByType<TreasureScenePresenter>()))[team];
        private static void Click(ScreenGameUi ui,string name)=>FindRect(ui,name).GetComponent<UiHitTarget>().Interact(PointerSource.Desktop);
        private static void AssertReachable(ScreenGameUi ui,string name)
        {
            var rect=FindRect(ui,name); var button=rect.GetComponent<Button>();
            Require(button.gameObject.activeInHierarchy && button.interactable,name+" active");
            var delta=rect.position-Camera.main.transform.position;
            Require(delta.magnitude<8,name+" ray range");
            var viewport=Camera.main.WorldToViewportPoint(rect.position);
            Require(viewport.z>0 && viewport.x>0.05 && viewport.x<0.95 && viewport.y>0.05 && viewport.y<0.95,name+" in view "+viewport);
            Physics.SyncTransforms();
            Require(Physics.RaycastAll(Camera.main.transform.position,delta.normalized,8).Any(h=>h.collider.GetComponent<UiHitTarget>()==rect.GetComponent<UiHitTarget>()),name+" collider reachable");
        }
        private static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        private static void Call(object target,string name)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);
        private static void Require(bool condition,string message){if(!condition)throw new Exception("[ComfortQA] "+message);}
        private static void Finish(int code)
        {
            SessionState.SetBool("ComfortQA",false); EditorApplication.update-=Tick;
            EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.EnteredEditMode)EditorApplication.delayCall+=()=>EditorApplication.Exit(code);};
            EditorApplication.isPlaying=false;
        }
        private static void Capture(string name)
        {
            const string folder="VisualQA/Gameplay-20260909-Finale-Wildcards"; Directory.CreateDirectory(folder);
            var camera=Camera.main; var prev=camera.targetTexture; var active=RenderTexture.active;
            var target=new RenderTexture(1600,1000,24);camera.targetTexture=target;Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
            var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();
            File.WriteAllBytes(folder+"/"+name+".png",image.EncodeToPNG());camera.targetTexture=prev;RenderTexture.active=active;
            UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
