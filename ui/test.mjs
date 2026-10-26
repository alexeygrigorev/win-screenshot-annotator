import { chromium } from '@playwright/test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
const root=path.resolve(import.meta.dirname,'../src/Annotator/Web');
const server=http.createServer((req,res)=>{
 const name=path.basename(req.url.split('?')[0])||'index.html';
 const file=path.join(root,name);
 if(!fs.existsSync(file)){res.writeHead(404);res.end();return;}
 res.setHeader('Content-Type',name.endsWith('.woff2')?'font/woff2':name.endsWith('.js')?'text/javascript':name.endsWith('.css')?'text/css':'text/html');
 res.end(fs.readFileSync(file));
}).listen(0,'127.0.0.1');
await new Promise(r=>server.once('listening',r));
const b=await chromium.launch(process.env.CHROMIUM_PATH?{executablePath:process.env.CHROMIUM_PATH}:{});
try {
 const page=await b.newPage({viewport:{width:960,height:760},deviceScaleFactor:1});
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(`http://127.0.0.1:${server.address().port}`);
 await page.waitForFunction(()=>!!window.annotator);
 await page.evaluate(()=>document.fonts.ready);
 assert.equal(await page.evaluate(()=>[...document.fonts].some(f=>f.family.includes('Inter Variable') && f.status==='loaded')),true);
 const url='data:image/png;base64,'+fs.readFileSync(path.resolve(import.meta.dirname,'../src/Annotator/ParitySample.png')).toString('base64');
 await page.evaluate(url=>window.annotator.load({url,width:900,height:550},1),url);
 const c=page.locator('canvas');const box=await c.boundingBox();
 assert.equal(box.width,900);assert.equal(box.height,550);
 async function drag(tool,a,z,shift=false){
  await page.getByRole('button',{name:tool,exact:true}).click();
  await page.mouse.move(box.x+a[0],box.y+a[1]);await page.mouse.down();
  if(shift)await page.keyboard.down('Shift');
  if(a[0]!==z[0]||a[1]!==z[1])await page.mouse.move(box.x+z[0],box.y+z[1],{steps:10});await page.mouse.up();
  if(shift)await page.keyboard.up('Shift');
 }
 await drag('Arrow',[80,280],[260,210]);await drag('Rectangle',[300,160],[650,330]);await drag('Draw',[100,400],[100,400]);
 await drag('Text',[357,200],[357,200]);
 await page.locator('textarea').fill('Look here\nMultiline caption');
 assert.equal(await page.locator('textarea').evaluate(e=>getComputedStyle(e).fontSize),'36px');
 await page.keyboard.press('Control+Enter');
 assert.equal(await page.evaluate(()=>window.annotator.state().items.length),4);
 await page.getByRole('button',{name:'Arrow',exact:true}).click();
 fs.mkdirSync(path.resolve(import.meta.dirname,'../artifacts/ui'),{recursive:true});
 await page.screenshot({path:path.resolve(import.meta.dirname,'../artifacts/ui/editor.png')});
 await drag('Text',[360,210],[360,210]);assert.equal(await page.locator('textarea').inputValue(),'Look here\nMultiline caption');
 await page.locator('textarea').fill('Edited');await page.keyboard.press('Escape');await page.keyboard.press('Control+z');
 assert.equal(await page.evaluate(()=>window.annotator.state().items[3].text),'Look here\nMultiline caption');
 await page.getByTitle('Clear',{exact:true}).click();assert.equal(await page.evaluate(()=>window.annotator.state().items.length),0);
 await page.keyboard.press('Control+z');assert.equal(await page.evaluate(()=>window.annotator.state().items.length),4);
 await page.keyboard.press('Escape');assert.equal(await page.locator('.actions.confirm').count(),1);
 await page.keyboard.press('Escape');assert.equal(await page.locator('.actions.confirm').count(),0);
 await drag('Line',[50,50],[150,66],true);
 const line=await page.evaluate(()=>window.annotator.state().items.at(-1));assert.equal(line.points[0].y,line.points[1].y);
 const scaled=await b.newPage({viewport:{width:960,height:760},deviceScaleFactor:1.5});
 await scaled.goto(`http://127.0.0.1:${server.address().port}`);
 await scaled.waitForFunction(()=>!!window.annotator);
 await scaled.evaluate(url=>window.annotator.load({url,width:900,height:550},1/devicePixelRatio),url);
 const physical=await scaled.evaluate(()=>{const s=window.annotator.state();return {width:s.sheet.width*devicePixelRatio,height:s.sheet.height*devicePixelRatio,toolbarButton:document.querySelector('.tool-btn').getBoundingClientRect().width*devicePixelRatio};});
 assert.equal(Math.round(physical.width),900);assert.equal(Math.round(physical.height),550);assert.equal(physical.toolbarButton,36);
 await scaled.close();
 console.log('PASS 150% renderer scale: sheet is 900x550 physical pixels; toolbar follows OS scale');
 assert.deepEqual(errors,[]);
 console.log('PASS actual DoodleCanvas: geometry, 36px caption, text edit, undo, clear, discard guard, Shift snapping');
} finally {await b.close();server.close();}
