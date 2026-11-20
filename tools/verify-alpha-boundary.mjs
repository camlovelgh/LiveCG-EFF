// Isolated, portable OBS integration test. Never uses the user's profile or starts a stream/recording.
import fs from 'node:fs/promises';import path from 'node:path';import {spawn,spawnSync} from 'node:child_process';import crypto from 'node:crypto';
const root=path.resolve(process.argv[2]??'.'), portable=path.resolve(root,process.env.LIVECG_OBS_PORTABLE??'artifacts/v010-obs-portable'),config=path.join(portable,'config/obs-studio'),password=crypto.randomBytes(24).toString('hex'),port=4469,results=[];
const sleep=ms=>new Promise(r=>setTimeout(r,ms));const check=(ok,name)=>{results.push({ok:!!ok,name});console.log((ok?'PASS ':'FAIL ')+name);};
try{await fs.access(path.join(portable,'bin/64bit/obs64.exe'));}catch{
    const installed=process.env.LIVECG_OBS_INSTALL??'C:/Program Files/obs-studio';
    await fs.access(path.join(installed,'bin/64bit/obs64.exe')).catch(()=>{throw new Error('OBS 설치 경로가 없습니다. LIVECG_OBS_INSTALL 환경변수로 OBS33 설치 폴더를 지정하세요.');});
    await fs.cp(installed,portable,{recursive:true});await fs.writeFile(path.join(portable,'portable_mode.txt'),'');
}
await fs.mkdir(path.join(config,'plugin_config/obs-websocket'),{recursive:true});
await fs.writeFile(path.join(config,'plugin_config/obs-websocket/config.json'),JSON.stringify({server_enabled:true,server_port:port,auth_required:true,server_password:password,alerts_enabled:false,first_load:false}));
await fs.writeFile(path.join(config,'global.ini'),'[General]\nFirstRun=false\nLastVersion=553648128\nEnableAutoUpdates=false\n\n[Basic]\nProfile=LiveCGTest\nProfileDir=LiveCGTest\nSceneCollection=LiveCGTest\nSceneCollectionFile=LiveCGTest\n\n[BasicWindow]\nStudioMode=true\n');
await fs.mkdir(path.join(config,'basic/profiles/LiveCGTest'),{recursive:true});await fs.writeFile(path.join(config,'basic/profiles/LiveCGTest/basic.ini'),'[General]\nName=LiveCGTest\n[Video]\nBaseCX=1080\nBaseCY=1920\nOutputCX=1080\nOutputCY=1920\nFPSType=0\nFPSCommon=30\n');

const fixture=path.join(root,'artifacts/alpha-boundary');
await fs.mkdir(fixture,{recursive:true});
const source=path.resolve(process.env.LIVECG_ALPHA_SOURCE??'C:/Users/lumos/Desktop/새 폴더/2611151821 뉴트리 6.png');
await fs.copyFile(source,path.join(fixture,'source.png'));
await fs.writeFile(path.join(fixture,'canvas.html'),'<!-- LiveCG-EFF alpha regression --><style>html,body{margin:0;background:transparent}canvas{display:block;transform:translateY(0.5px)}</style><canvas width="1080" height="1920"></canvas><script>const i=new Image;i.onload=()=>document.querySelector("canvas").getContext("2d").drawImage(i,0,0);i.src="source.png"</script>');
const exported=process.env.LIVECG_ALPHA_HTML??path.join(path.dirname(source),'OBS 출력',path.basename(source,path.extname(source))+'.html');
const entry=await fs.readFile(exported,'utf8');
const identity=entry.match(/LiveCG-EFF linked source ([a-f0-9]{32})/);
if(!identity)throw new Error('Expected a LiveCG linked-source HTML entry');
const runtime=path.join(fixture,'runtime');
await fs.mkdir(runtime,{recursive:true});
await fs.copyFile(exported,path.join(runtime,path.basename(exported)));
await fs.cp(path.join(path.dirname(exported),'.livecg',identity[1]),path.join(runtime,'.livecg',identity[1]),{recursive:true});
const revisions=path.join(runtime,'.livecg',identity[1],'revisions');
for(const revision of await fs.readdir(revisions)){
 const index=path.join(revisions,revision,'index.html');
 let html=await fs.readFile(index,'utf8');
 html=html.replace('canvas{display:block;','canvas{transform:translateY(0.5px);display:block;');
 html=html.replace('</body>','<script>setInterval(()=>{if(window.LiveCG?.status().loaded)LiveCG.renderAt(0.4)},50)</script></body>');
 await fs.writeFile(index,html);
}
const collection='AlphaSetup'+Date.now();
await fs.mkdir(path.join(config,'basic/scenes'),{recursive:true});
await fs.writeFile(path.join(config,'basic/scenes',collection+'.json'),JSON.stringify({
 name:collection,current_scene:'Empty',current_program_scene:'Empty',scene_order:[{name:'Empty'}],
 sources:[{name:'Empty',uuid:crypto.randomUUID(),id:'scene',versioned_id:'scene',enabled:true,settings:{items:[]}}],
 modules:{'scripts-tool':[{path:path.join(root,'tools/LiveCG-OBS-Hotkeys.lua'),settings:{fix_alpha:!process.argv.includes('--baseline')}}]}
}));
const child=spawn(path.join(portable,'bin/64bit/obs64.exe'),['--portable','--multi','--disable-updater','--disable-missing-files-check','--minimize-to-tray'],{cwd:path.join(portable,'bin/64bit'),windowsHide:true,stdio:'ignore'});
let ws,seq=0;const pending=new Map();let logFile;const renderCaptures=[];
function request(requestType,requestData={}){return new Promise((resolve,reject)=>{const id=String(++seq),timer=setTimeout(()=>{pending.delete(id);reject(new Error('OBS timeout '+requestType));},7000);pending.set(id,{resolve,reject,timer});ws.send(JSON.stringify({op:6,d:{requestType,requestId:id,requestData}}));});}
async function connect(){
    for(let i=0;i<160;i++){
        try{ws=await new Promise((resolve,reject)=>{const socket=new WebSocket(`ws://127.0.0.1:${port}`);socket.addEventListener('error',()=>reject(new Error('connecting')),{once:true});socket.addEventListener('open',()=>resolve(socket),{once:true});});break;}catch{await sleep(100);}
    }
    if(!ws)throw new Error('Isolated OBS WebSocket did not start');
    await new Promise((resolve,reject)=>{ws.addEventListener('message',event=>{const m=JSON.parse(event.data);if(m.op===0){const a=m.d.authentication;let authentication;if(a){const secret=crypto.createHash('sha256').update(password+a.salt).digest('base64');authentication=crypto.createHash('sha256').update(secret+a.challenge).digest('base64');}ws.send(JSON.stringify({op:1,d:{rpcVersion:1,eventSubscriptions:0,...(authentication?{authentication}:{})}}));}else if(m.op===2)resolve();else if(m.op===7){const p=pending.get(m.d.requestId);if(p){clearTimeout(p.timer);pending.delete(m.d.requestId);m.d.requestStatus.result?p.resolve(m.d.responseData):p.reject(new Error(m.d.requestType+': '+JSON.stringify(m.d.requestStatus)));}}});ws.addEventListener('close',()=>reject(new Error('OBS connection closed')));});
}

try{
 await connect();
 await sleep(1000);
 await request('SetVideoSettings',{baseWidth:1080,baseHeight:1920,outputWidth:1080,outputHeight:1920});
 await sleep(500);
 await request('SetCurrentSceneCollection',{sceneCollectionName:collection});
 for(const name of ['Image','Canvas','Runtime'])await request('CreateScene',{sceneName:name});
 await request('SetStudioModeEnabled',{studioModeEnabled:false});
 const html=path.join(runtime,path.basename(exported));
 for(const [name,kind,settings] of [
  ['Image','image_source',{file:source}],
  ['Canvas','browser_source',{is_local_file:true,local_file:path.join(fixture,'canvas.html'),width:1080,height:1920,shutdown:false}],
  ['Runtime','browser_source',{is_local_file:true,local_file:html,width:1080,height:1920,shutdown:false}]
 ]){
  await request('SetCurrentProgramScene',{sceneName:name});
  await request('CreateInput',{sceneName:name,inputName:name+' white',inputKind:'color_source_v3',inputSettings:{color:4294967295,width:1080,height:1920},sceneItemEnabled:true});
  const added=await request('CreateInput',{sceneName:name,inputName:name+' source',inputKind:kind,inputSettings:settings,sceneItemEnabled:true});
  await sleep(1600);
  for(const [tag,sx,sy] of [['native',1,1],['small',.4398,.4398],['large',1.615,1.615]]){
   await request('SetSceneItemTransform',{sceneName:name,sceneItemId:added.sceneItemId,sceneItemTransform:{scaleX:sx,scaleY:sy,positionX:0.3,positionY:0.2}});
   await sleep(1150);
   const shot=await request('GetSourceScreenshot',{sourceName:name,imageFormat:'png',imageWidth:1080,imageHeight:1920});
   await fs.writeFile(path.join(fixture,name+(process.argv.includes('--baseline')?'-baseline-':'-fixed-')+tag+'.png'),Buffer.from(shot.imageData.split(',')[1],'base64'));
  }
 }
}finally{ws?.close();child.kill();}
