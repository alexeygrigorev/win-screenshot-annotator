import { createApp, h, ref, nextTick } from 'vue';
import DoodleCanvas from './vendor/app/components/DoodleCanvas.vue';
import './vendor/tokens.css';
import './host.css';
const bridge = message => window.chrome?.webview?.postMessage(message);
const source = ref(null), component = ref(null);
let action = 'copy';
const rect = el => { const r=el.getBoundingClientRect(); return {x:r.x,y:r.y,width:r.width,height:r.height}; };
window.annotator = {
  async load(data, scale) {
    document.documentElement.style.setProperty('--sheet-width', `${data.width * scale}px`);
    document.documentElement.style.setProperty('--sheet-height', `${data.height * scale}px`);
    source.value=data.url;
    await nextTick();
    const c=document.querySelector('canvas');
    for(let i=0;i<300;i++) {
      if(c.width===data.width && c.height===data.height) break;
      await new Promise(r=>setTimeout(r,10));
    }
    await new Promise(requestAnimationFrame);
    bridge({kind:'loaded',sheet:rect(c),dpr:devicePixelRatio});
  },
  close: () => component.value.requestClose(),
  async export(kind) { action=kind; await component.value.commit(); },
  state: () => ({items:component.value.items,editing:component.value.editing,tool:component.value.tool,pen:component.value.pen,width:component.value.width,sheet:rect(document.querySelector('canvas')),dpr:devicePixelRatio}),
};
createApp({setup:()=>()=>h('main',[
 h('header',{class:'title'},[h('span','Annotate'),h('button',{'aria-label':'Close',onClick:()=>window.annotator.close()},'×')]),
 h(DoodleCanvas,{ref:component,backdrop:source.value,backdropName:'Screenshot',onClose:()=>bridge({kind:'close'}),onSave:()=>window.annotator.export('save'),onCommit:p=>{bridge({kind:action,png:p.dataUrl});action='copy';}})
])}).mount('#app');
document.addEventListener('keydown',e=>{
 if(e.key==='Escape' && !e.defaultPrevented && e.target.tagName!=='TEXTAREA'){e.preventDefault();window.annotator.close();}
 if(e.ctrlKey && e.key.toLowerCase()==='s'){e.preventDefault();window.annotator.export('save');}
 if(e.ctrlKey && e.key.toLowerCase()==='c' && e.target.tagName!=='TEXTAREA'){e.preventDefault();window.annotator.export('copy');}
});
bridge({kind:'ready'});
