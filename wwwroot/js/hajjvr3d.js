// ============================================================
// HajjVR 3D Engine — Three.js (v2, enhanced realism)
// Layout berdasarkan referensi nyata:
//  - Ka'bah 12.9×11×13.1 m, sudut menghadap arah mata angin,
//    Hijr Ismail 8.46 m dari dinding barat-laut, Maqam Ibrahim ±11 m
//    di timur pintu, hizam (band emas) di 2/3 tinggi.
//  - Mas'a (Safa→Marwah) ±450 m di sisi timur, Safa di tenggara,
//    Marwah di timur-laut Ka'bah. 13 menara. Abraj Al-Bait di selatan.
//  - Nabawi: Kubah Hijau di pojok tenggara, 27 kubah geser,
//    payung persegi raksasa di pelataran, 10 menara.
//  - Jamarat: jembatan multi-lantai, 3 pilar elips; Mina: kota tenda;
//    Arafah: Jabal Rahmah + tugu putih + Masjid Namirah.
// ============================================================
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { PointerLockControls } from 'three/addons/controls/PointerLockControls.js';
import { VRButton } from 'three/addons/webxr/VRButton.js';

let renderer, scene, camera, orbit, plc, clock;
let container, canvas;
let bodyMesh = null, headMesh = null, agents = [], crowdCount = 1500;
let sceneKey = 'haram', cameraMode = 'orbit', timeOfDay = 10;
let sun, hemi, ambient, sunDisc, moonDisc, stars, skyDome, cloudMesh;
let staticGroup = null;
let nightMaterials = [];   // material dengan emissive jendela/lampu yang menyala di malam hari
let birdMesh = null, birds = [];
let simSpeed = 1, paused = false, autoTime = false, autoTimeSpeed = 0.35; // jam simulasi per detik
let simTime = 0; // waktu simulasi terakumulasi (ikut simSpeed & pause)
let camGoal = null;
let shadowsOn = false;
let followIdx = 0;
let keys = {};
let fps = 0, frames = 0, lastFpsTime = 0;
let disposed = false;

// Titik fokus kamera (POI) per scene — dipakai tombol "fokus lokasi"
const POIS = {
    haram: {
        kaaba: { t: [0, 10, 0], d: 130 },
        hijr: { t: [-10, 3, 0], d: 40 },
        maqam: { t: [13.5, 2, 4.5], d: 30 },
        safa: { t: [112, 4, 178], d: 70 },
        marwah: { t: [196, 4, -248], d: 70 },
        masaa: { t: [154, 8, -35], d: 240 },
        abraj: { t: [20, 130, 330], d: 300 }
    },
    nabawi: {
        masjid: { t: [0, 12, 20], d: 280 },
        kubahhijau: { t: [78, 18, 120], d: 85 },
        raudhah: { t: [70, 2, 150], d: 60 },
        payung: { t: [0, 12, -190], d: 150 }
    },
    manasik: {
        jamarat: { t: [0, 10, 60], d: 160 },
        mina: { t: [260, 6, -30], d: 240 },
        arafah: { t: [-300, 14, -60], d: 200 },
        muzdalifah: { t: [-60, 6, -160], d: 150 },
        namirah: { t: [-350, 8, 90], d: 120 }
    }
};

const dummy = new THREE.Object3D();
const SKY_DAY = new THREE.Color(0x8ec8e8);
const SKY_SET = new THREE.Color(0xe8956a);
const SKY_NIGHT = new THREE.Color(0x070b1e);

// ---------- Tekstur prosedural (tanpa aset eksternal) ----------
const texCache = {};
function canvasTex(key, size, draw, repeat = 1) {
    if (texCache[key]) return texCache[key];
    const c = document.createElement('canvas');
    c.width = c.height = size;
    draw(c.getContext('2d'), size);
    const t = new THREE.CanvasTexture(c);
    t.wrapS = t.wrapT = THREE.RepeatWrapping;
    t.repeat.set(repeat, repeat);
    t.anisotropy = 4;
    texCache[key] = t;
    return t;
}

function marbleTex(repeat = 24) {
    return canvasTex('marble' + repeat, 256, (g, s) => {
        g.fillStyle = '#efece3'; g.fillRect(0, 0, s, s);
        for (let i = 0; i < 240; i++) { // urat marmer
            g.strokeStyle = `rgba(${150 + Math.random() * 60},${150 + Math.random() * 55},${140 + Math.random() * 50},${.12 + Math.random() * .1})`;
            g.lineWidth = .6 + Math.random();
            g.beginPath();
            let x = Math.random() * s, y = Math.random() * s;
            g.moveTo(x, y);
            for (let k = 0; k < 4; k++) { x += (Math.random() - .5) * 40; y += (Math.random() - .5) * 40; g.lineTo(x, y); }
            g.stroke();
        }
        g.strokeStyle = 'rgba(120,115,100,.35)'; g.lineWidth = 2;   // nat ubin
        for (let i = 0; i <= 4; i++) {
            g.beginPath(); g.moveTo(i * s / 4, 0); g.lineTo(i * s / 4, s); g.stroke();
            g.beginPath(); g.moveTo(0, i * s / 4); g.lineTo(s, i * s / 4); g.stroke();
        }
    }, repeat);
}

function sandTex(repeat = 40) {
    return canvasTex('sand' + repeat, 256, (g, s) => {
        g.fillStyle = '#cdb388'; g.fillRect(0, 0, s, s);
        for (let i = 0; i < 5000; i++) {
            g.fillStyle = `rgba(${120 + Math.random() * 90},${95 + Math.random() * 70},${55 + Math.random() * 50},${.16})`;
            g.fillRect(Math.random() * s, Math.random() * s, 1.6, 1.6);
        }
    }, repeat);
}

function kiswaTex() {
    return canvasTex('kiswa', 256, (g, s) => {
        g.fillStyle = '#0b0b0d'; g.fillRect(0, 0, s, s);
        g.strokeStyle = 'rgba(40,40,46,.9)'; g.lineWidth = 1;   // tenunan halus
        for (let y = 0; y < s; y += 5) { g.beginPath(); g.moveTo(0, y); g.lineTo(s, y); g.stroke(); }
        g.strokeStyle = 'rgba(58,54,40,.5)';                    // pola belah ketupat samar
        for (let i = -s; i < s * 2; i += 32) {
            g.beginPath(); g.moveTo(i, 0); g.lineTo(i + s, s); g.stroke();
            g.beginPath(); g.moveTo(i + s, 0); g.lineTo(i, s); g.stroke();
        }
    }, 2);
}

function hizamTex() { // band emas dengan goresan kaligrafi
    return canvasTex('hizam', 512, (g, s) => {
        g.fillStyle = '#101010'; g.fillRect(0, 0, s, s);
        g.fillStyle = '#0d0d0d'; g.fillRect(0, s * .28, s, s * .44);
        g.strokeStyle = '#c9a227'; g.lineWidth = 4;
        g.strokeRect(6, s * .3, s - 12, s * .4);
        g.strokeStyle = '#d9b544'; g.lineWidth = 3; g.lineCap = 'round';
        let x = 18;
        while (x < s - 20) { // goresan menyerupai kaligrafi tsuluts
            const w = 14 + Math.random() * 26, base = s * .5;
            g.beginPath();
            g.moveTo(x, base + (Math.random() - .5) * 14);
            g.bezierCurveTo(x + w * .3, base - 22 - Math.random() * 16, x + w * .6, base + 16, x + w, base - 6 + Math.random() * 10);
            g.stroke();
            if (Math.random() < .5) { g.beginPath(); g.arc(x + w * .5, base - 24, 2, 0, 7); g.stroke(); }
            x += w + 6;
        }
    }, 6);
}

function buildingTex() { // fasad gedung dengan jendela (emissiveMap yang sama → menyala malam)
    return canvasTex('bld', 256, (g, s) => {
        g.fillStyle = '#8f8a80'; g.fillRect(0, 0, s, s);
        for (let y = 8; y < s - 8; y += 22) {
            for (let x = 8; x < s - 8; x += 18) {
                g.fillStyle = Math.random() < .55 ? '#ffe9a8' : '#1c2230';
                g.fillRect(x, y, 10, 13);
            }
        }
    }, 1);
}

function arcadeTex() { // deretan lengkungan (arch) untuk fasad masjid
    return canvasTex('arcade', 512, (g, s) => {
        g.fillStyle = '#efe9d8'; g.fillRect(0, 0, s, s);
        const n = 6, w = s / n;
        for (let i = 0; i < n; i++) {
            const cx = i * w + w / 2;
            g.fillStyle = '#4a4438';
            g.beginPath();
            g.moveTo(cx - w * .3, s);
            g.lineTo(cx - w * .3, s * .45);
            g.arc(cx, s * .45, w * .3, Math.PI, 0);
            g.lineTo(cx + w * .3, s);
            g.closePath(); g.fill();
            g.strokeStyle = '#c9a227'; g.lineWidth = 3;
            g.beginPath(); g.arc(cx, s * .45, w * .34, Math.PI, 0); g.stroke();
        }
        g.fillStyle = '#ddd5bf'; g.fillRect(0, 0, s, s * .12);
    }, 8);
}

// ---------- Util material ----------
function mat(color, opts = {}) { return new THREE.MeshStandardMaterial({ color, roughness: .85, metalness: .05, ...opts }); }
function gold(opts = {}) { return mat(0xc9a227, { metalness: .75, roughness: .3, ...opts }); }

/** InstancedMesh dari daftar transformasi {p:[x,y,z], r:[rx,ry,rz], s:[sx,sy,sz]} */
function inst(geo, material, items) {
    const m = new THREE.InstancedMesh(geo, material, items.length);
    items.forEach((it, i) => {
        dummy.position.set(...(it.p || [0, 0, 0]));
        dummy.rotation.set(...(it.r || [0, 0, 0]));
        dummy.scale.set(...(it.s || [1, 1, 1]));
        dummy.updateMatrix();
        m.setMatrixAt(i, dummy.matrix);
        if (it.c) m.setColorAt(i, new THREE.Color(it.c));
    });
    if (m.instanceColor) m.instanceColor.needsUpdate = true;
    staticGroup.add(m);
    return m;
}

// ---------- Inisialisasi ----------
export function init(canvasEl, opts = {}) {
    disposed = false;
    canvas = canvasEl;
    container = canvas.parentElement;
    sceneKey = opts.scene || 'haram';
    crowdCount = opts.crowd || 1500;
    timeOfDay = opts.timeOfDay ?? 10;

    renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.05;
    renderer.shadowMap.enabled = false;
    renderer.xr.enabled = true;

    scene = new THREE.Scene();
    scene.fog = new THREE.Fog(0x8ec8e8, 300, 1400);

    camera = new THREE.PerspectiveCamera(58, 1, 0.1, 3000);
    camera.position.set(110, 75, 150);
    clock = new THREE.Clock();

    hemi = new THREE.HemisphereLight(0xffffff, 0x9a8666, 0.85);
    scene.add(hemi);
    sun = new THREE.DirectionalLight(0xfff2d9, 2.1);
    scene.add(sun);
    ambient = new THREE.AmbientLight(0xffffff, 0.22);
    scene.add(ambient);

    // Matahari & bulan (piringan visual)
    sunDisc = new THREE.Mesh(new THREE.SphereGeometry(22, 16, 12),
        new THREE.MeshBasicMaterial({ color: 0xfff3b0, fog: false }));
    moonDisc = new THREE.Mesh(new THREE.SphereGeometry(14, 16, 12),
        new THREE.MeshBasicMaterial({ color: 0xdfe6f0, fog: false }));
    scene.add(sunDisc, moonDisc);

    // Bintang (muncul di malam hari)
    const starGeo = new THREE.BufferGeometry();
    const starPos = [];
    for (let i = 0; i < 1600; i++) {
        const a = Math.random() * Math.PI * 2, e = Math.random() * Math.PI * .48 + .05, r = 1900;
        starPos.push(Math.cos(a) * Math.cos(e) * r, Math.sin(e) * r, Math.sin(a) * Math.cos(e) * r);
    }
    starGeo.setAttribute('position', new THREE.Float32BufferAttribute(starPos, 3));
    stars = new THREE.Points(starGeo, new THREE.PointsMaterial({
        color: 0xffffff, size: 2.2, sizeAttenuation: false, transparent: true, opacity: 0, fog: false
    }));
    scene.add(stars);

    // Kubah langit gradient (zenith → horizon), diperbarui oleh applyTime
    skyDome = new THREE.Mesh(
        new THREE.SphereGeometry(2100, 24, 12),
        new THREE.ShaderMaterial({
            side: THREE.BackSide, depthWrite: false, fog: false,
            uniforms: {
                top: { value: new THREE.Color(0x4d9be0) },
                horizon: { value: new THREE.Color(0xbfe3f5) }
            },
            vertexShader: 'varying vec3 vP; void main(){ vP = position; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
            fragmentShader: `varying vec3 vP; uniform vec3 top; uniform vec3 horizon;
                void main(){
                    float h = normalize(vP).y;
                    float t = smoothstep(0.0, 0.45, max(h, 0.0));
                    gl_FragColor = vec4(mix(horizon, top, t), 1.0);
                }`
        }));
    skyDome.renderOrder = -1;
    scene.add(skyDome);

    // Awan tipis melayang (instanced, drift pelan)
    const cloudItems = [];
    for (let i = 0; i < 22; i++) {
        const a = Math.random() * Math.PI * 2, r = 250 + Math.random() * 650;
        cloudItems.push({
            x: Math.cos(a) * r, y: 190 + Math.random() * 160, z: Math.sin(a) * r,
            sx: 3 + Math.random() * 5, sy: .5 + Math.random() * .5, sz: 2 + Math.random() * 3
        });
    }
    cloudMesh = new THREE.InstancedMesh(
        new THREE.SphereGeometry(14, 10, 7),
        new THREE.MeshLambertMaterial({ color: 0xffffff, transparent: true, opacity: .78 }),
        cloudItems.length);
    cloudItems.forEach((c, i) => {
        dummy.position.set(c.x, c.y, c.z);
        dummy.rotation.set(0, Math.random() * Math.PI, 0);
        dummy.scale.set(c.sx, c.sy, c.sz);
        dummy.updateMatrix();
        cloudMesh.setMatrixAt(i, dummy.matrix);
    });
    scene.add(cloudMesh);

    orbit = new OrbitControls(camera, canvas);
    orbit.target.set(0, 8, 0);
    orbit.maxPolarAngle = Math.PI / 2.05;
    orbit.maxDistance = 1200;
    orbit.enableDamping = true;

    plc = new PointerLockControls(camera, canvas);
    window.addEventListener('keydown', onKey, false);
    window.addEventListener('keyup', onKeyUp, false);

    buildScene(sceneKey);
    buildCrowd(crowdCount);
    applyTime(timeOfDay);
    resize();
    new ResizeObserver(resize).observe(container);

    try {
        const vrBtn = VRButton.createButton(renderer);
        vrBtn.style.position = 'absolute';
        vrBtn.style.bottom = '14px';
        container.appendChild(vrBtn);
    } catch (e) { console.warn('WebXR tidak tersedia:', e); }

    renderer.setAnimationLoop(animate);
    return true;
}

function resize() {
    if (!renderer || disposed) return;
    const w = container.clientWidth, h = container.clientHeight;
    renderer.setSize(w, h, false);
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
}

function clearStatic() {
    if (staticGroup) {
        scene.remove(staticGroup);
        staticGroup.traverse(o => { o.geometry?.dispose?.(); });
    }
    staticGroup = new THREE.Group();
    nightMaterials = [];
    birdMesh = null;
    birds = [];
    camGoal = null;
    scene.add(staticGroup);
}

function buildScene(key) {
    sceneKey = key;
    clearStatic();
    if (key === 'nabawi') buildNabawi();
    else if (key === 'manasik') buildManasik();
    else buildHaram();
    rebuildAgents();
    applyTime(timeOfDay);
}

// ============================================================
//  PROPS BERSAMA
// ============================================================

/** Menara masjid gaya Saudi: dasar persegi, badan silinder, 2 balkon, mahkota emas + bulan sabit */
function addMinaret(x, z, h = 89, style = 'haram') {
    const g = new THREE.Group();
    const stone = mat(0xefe7d3, { roughness: .7 });
    const base = new THREE.Mesh(new THREE.BoxGeometry(7, h * .34, 7), stone);
    base.position.y = h * .17;
    const mid = new THREE.Mesh(new THREE.CylinderGeometry(2.6, 3.1, h * .42, 10), stone);
    mid.position.y = h * .34 + h * .21;
    const balc1 = new THREE.Mesh(new THREE.CylinderGeometry(4.2, 4.2, 1.4, 10), mat(0xd9cfb4));
    balc1.position.y = h * .34;
    const balc2 = balc1.clone();
    balc2.position.y = h * .34 + h * .42;
    const top = new THREE.Mesh(new THREE.CylinderGeometry(1.5, 2.2, h * .16, 10), stone);
    top.position.y = h * .34 + h * .42 + h * .08;
    const cap = new THREE.Mesh(new THREE.ConeGeometry(2.4, h * .1, 10), style === 'nabawi' ? mat(0x0e6b39, { roughness: .4 }) : gold());
    cap.position.y = h * .92 + h * .05;
    const ball = new THREE.Mesh(new THREE.SphereGeometry(.9, 8, 8), gold());
    ball.position.y = h * 1.02;
    const crescent = new THREE.Mesh(new THREE.TorusGeometry(1.4, .22, 6, 16, Math.PI * 1.4), gold());
    crescent.position.y = h * 1.02 + 2.4;
    crescent.rotation.z = Math.PI * .8;
    // lampu balkon menyala di malam hari
    const lampMat = new THREE.MeshStandardMaterial({ color: 0x2a2a20, emissive: 0x9fdc7a, emissiveIntensity: 0 });
    nightMaterials.push(lampMat);
    const lamp = new THREE.Mesh(new THREE.CylinderGeometry(4.35, 4.35, .5, 10), lampMat);
    lamp.position.y = h * .34 + .9;
    g.add(base, mid, balc1, balc2, top, cap, ball, crescent, lamp);
    g.position.set(x, 0, z);
    staticGroup.add(g);
}

/** Tiang lampu sorot stadion (untuk pelataran / Mina / Muzdalifah) */
function addFloodTower(x, z, h = 24) {
    const pole = new THREE.Mesh(new THREE.CylinderGeometry(.4, .6, h, 6), mat(0x777777, { metalness: .5 }));
    pole.position.set(x, h / 2, z);
    const headMat = new THREE.MeshStandardMaterial({ color: 0x555555, emissive: 0xfff2c9, emissiveIntensity: 0 });
    nightMaterials.push(headMat);
    const head = new THREE.Mesh(new THREE.BoxGeometry(3.4, 1.1, 1.4), headMat);
    head.position.set(x, h + .4, z);
    staticGroup.add(pole, head);
}

/** Cincin pegunungan berbatu di sekeliling kota */
function addMountains(rMin = 620, rMax = 900, n = 46, hMax = 130) {
    const items = [];
    for (let i = 0; i < n; i++) {
        const a = (i / n) * Math.PI * 2 + Math.random() * .12;
        const r = rMin + Math.random() * (rMax - rMin);
        const h = 40 + Math.random() * hMax;
        items.push({
            p: [Math.cos(a) * r, -4, Math.sin(a) * r],
            r: [0, Math.random() * Math.PI, 0],
            s: [1 + Math.random() * 1.6, h / 60, 1 + Math.random() * 1.6],
            c: new THREE.Color().setHSL(.08, .18 + Math.random() * .1, .28 + Math.random() * .1)
        });
    }
    const geo = new THREE.ConeGeometry(60, 60, 7);
    const m = inst(geo, mat(0x8a755a, { roughness: 1, flatShading: true }), items);
    m.instanceColor && (m.instanceColor.needsUpdate = true);
}

/** Blok kota (hotel/apartemen) dengan tekstur jendela yang menyala malam hari */
function addCityBlocks(rMin, rMax, count, exclude = () => false) {
    const texture = buildingTex();
    const bmat = new THREE.MeshStandardMaterial({
        map: texture, emissiveMap: texture, emissive: 0xffffff, emissiveIntensity: 0, roughness: .9
    });
    nightMaterials.push(bmat);
    const items = [];
    let guard = 0;
    while (items.length < count && guard++ < count * 12) {
        const a = Math.random() * Math.PI * 2;
        const r = rMin + Math.random() * (rMax - rMin);
        const x = Math.cos(a) * r, z = Math.sin(a) * r;
        if (exclude(x, z)) continue;
        const h = 14 + Math.random() * 52;
        items.push({ p: [x, h / 2 - 2, z], r: [0, Math.random() * Math.PI, 0], s: [(10 + Math.random() * 16) / 10, h / 10, (10 + Math.random() * 16) / 10] });
    }
    inst(new THREE.BoxGeometry(10, 10, 10), bmat, items);
}

/** Kawanan merpati yang terbang berputar di atas pelataran */
function spawnBirds(n, centers) {
    birds = [];
    for (let i = 0; i < n; i++) {
        const c = centers[(Math.random() * centers.length) | 0];
        birds.push({
            cx: c[0] + (Math.random() - .5) * 60,
            cy: 26 + Math.random() * 45,
            cz: c[1] + (Math.random() - .5) * 60,
            r: 14 + Math.random() * 40,
            speed: .35 + Math.random() * .5,
            phase: Math.random() * Math.PI * 2,
            flap: 6 + Math.random() * 6
        });
    }
    const geo = new THREE.ConeGeometry(.5, 1.5, 3);
    geo.rotateX(Math.PI / 2); // ujung runcing ke depan (arah terbang)
    birdMesh = new THREE.InstancedMesh(geo, new THREE.MeshLambertMaterial({ color: 0x6d7076 }), n);
    birdMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
    staticGroup.add(birdMesh);
}

function updateBirds(t) {
    if (!birdMesh) return;
    for (let i = 0; i < birds.length; i++) {
        const b = birds[i];
        const a = b.phase + t * b.speed;
        dummy.position.set(
            b.cx + Math.cos(a) * b.r,
            b.cy + Math.sin(t * 1.6 + b.phase) * 3,
            b.cz + Math.sin(a) * b.r);
        dummy.rotation.set(0, -a, 0);
        // kepakan sayap: denyut lebar badan
        const flap = .55 + .45 * Math.abs(Math.sin(t * b.flap + b.phase));
        dummy.scale.set(1.6 * flap + .4, .5, 1);
        dummy.updateMatrix();
        birdMesh.setMatrixAt(i, dummy.matrix);
    }
    birdMesh.instanceMatrix.needsUpdate = true;
}

/** Pohon kurma */
function addPalms(spots) {
    const trunks = spots.map(([x, z]) => ({ p: [x, 2.6, z], r: [0, Math.random() * 3, (Math.random() - .5) * .12], s: [1, 1 + Math.random() * .4, 1] }));
    inst(new THREE.CylinderGeometry(.22, .38, 5.2, 6), mat(0x8a6a45, { roughness: 1 }), trunks);
    const crowns = [];
    spots.forEach(([x, z]) => {
        for (let k = 0; k < 5; k++) {
            const a = (k / 5) * Math.PI * 2;
            crowns.push({
                p: [x + Math.cos(a) * 1.1, 5.4, z + Math.sin(a) * 1.1],
                r: [Math.sin(a) * .9, 0, -Math.cos(a) * .9],
                s: [1, 1, 1]
            });
        }
    });
    inst(new THREE.ConeGeometry(.55, 3.2, 5), mat(0x3f7d32, { roughness: .9 }), crowns);
}

// ============================================================
//  SCENE 1: MASJIDIL HARAM
// ============================================================
function buildHaram() {
    // ---- Tanah & pelataran ----
    const ground = new THREE.Mesh(new THREE.CircleGeometry(1100, 64),
        new THREE.MeshStandardMaterial({ map: sandTex(70), roughness: 1 }));
    ground.rotation.x = -Math.PI / 2;
    ground.position.y = -1.2;
    staticGroup.add(ground);

    const plaza = new THREE.Mesh(new THREE.CylinderGeometry(240, 240, .6, 48),
        new THREE.MeshStandardMaterial({ map: marbleTex(60), roughness: .5 }));
    plaza.position.y = -0.85;
    staticGroup.add(plaza);

    // ---- Lantai mataf (marmer putih, sedikit lebih tinggi) ----
    const mataf = new THREE.Mesh(new THREE.CylinderGeometry(62, 62, 1, 64),
        new THREE.MeshStandardMaterial({ map: marbleTex(26), roughness: .35 }));
    mataf.position.y = -0.45;
    staticGroup.add(mataf);

    // ---- KA'BAH (12.9 × 13.1t × 11.0, sudut ke arah mata angin → rotasi 45°) ----
    const kaabaGroup = new THREE.Group();
    kaabaGroup.rotation.y = Math.PI / 4;

    const kiswa = new THREE.MeshStandardMaterial({ map: kiswaTex(), roughness: .75 });
    const kaaba = new THREE.Mesh(new THREE.BoxGeometry(12.9, 13.1, 11.0), kiswa);
    kaaba.position.y = 13.1 / 2 + .3;
    kaabaGroup.add(kaaba);

    // Hizam — band emas di ±2/3 tinggi
    const band = new THREE.Mesh(new THREE.BoxGeometry(13.05, 1.1, 11.15),
        new THREE.MeshStandardMaterial({ map: hizamTex(), roughness: .5, metalness: .3 }));
    band.position.y = 9.4;
    kaabaGroup.add(band);

    // Shadharwan — alas marmer miring
    const shad = new THREE.Mesh(new THREE.CylinderGeometry(9.6, 10.3, .65, 4, 1),
        mat(0xd9d2c0, { roughness: .5 }));
    shad.rotation.y = Math.PI / 4;
    shad.position.y = .32;
    kaabaGroup.add(shad);

    // Pintu emas (sisi timur-laut, +2.2 m dari tanah, dekat sudut timur)
    const door = new THREE.Mesh(new THREE.BoxGeometry(.25, 3.1, 1.9), gold({ roughness: .22 }));
    door.position.set(6.55, 2.2 + 1.55, 3.2);
    kaabaGroup.add(door);
    const doorFrame = new THREE.Mesh(new THREE.BoxGeometry(.18, 3.6, 2.4), gold({ roughness: .35 }));
    doorFrame.position.set(6.5, 2.2 + 1.65, 3.2);
    kaabaGroup.add(doorFrame);

    // Sitarah — tirai hitam-emas di atas pintu
    const sitarah = new THREE.Mesh(new THREE.PlaneGeometry(2.7, 3.4),
        new THREE.MeshStandardMaterial({ map: hizamTex(), roughness: .6 }));
    sitarah.rotation.y = Math.PI / 2;
    sitarah.position.set(6.62, 7.1, 3.2);
    kaabaGroup.add(sitarah);

    // Mizab Rahmah — talang emas di atap sisi barat-laut (menghadap Hijr)
    const mizab = new THREE.Mesh(new THREE.BoxGeometry(2.6, .35, .55), gold());
    mizab.position.set(-6.9, 13.35, 0);
    kaabaGroup.add(mizab);

    // Hajar Aswad — bingkai perak di sudut timur, 1.5 m
    const hajar = new THREE.Group();
    const silver = new THREE.Mesh(new THREE.TorusGeometry(.42, .16, 8, 18), mat(0xcfd2d6, { metalness: .85, roughness: .25 }));
    const stoneB = new THREE.Mesh(new THREE.SphereGeometry(.26, 10, 8), mat(0x141414, { roughness: .3 }));
    hajar.add(silver, stoneB);
    hajar.position.set(6.55, 1.5, -5.6);
    hajar.rotation.y = Math.PI / 4;
    kaabaGroup.add(hajar);

    staticGroup.add(kaabaGroup);

    // Garis awal thawaf (garis coklat dari sudut Hajar Aswad — arah timur)
    const startLine = new THREE.Mesh(new THREE.BoxGeometry(52, .06, .8), mat(0x7a5a35, { roughness: .6 }));
    startLine.position.set(36, .12, 0);
    staticGroup.add(startLine);

    // ---- Hijr Ismail (dinding setengah lingkaran 1.31 m, 8.46 m dari dinding barat-laut) ----
    const hijrShape = new THREE.Shape();
    hijrShape.absarc(0, 0, 8.46 + 1.5, Math.PI * .1, Math.PI * .9, false);
    hijrShape.absarc(0, 0, 8.46, Math.PI * .9, Math.PI * .1, true);
    const hijrGeo = new THREE.ExtrudeGeometry(hijrShape, { depth: 1.31, bevelEnabled: false });
    const hijr = new THREE.Mesh(hijrGeo, mat(0xf2eee2, { roughness: .35 }));
    // Rz(90°) memutar busur agar menonjol ke -X lokal (sisi barat-laut), lalu Rx(-90°) merebahkannya
    hijr.rotation.set(-Math.PI / 2, 0, Math.PI / 2);
    hijr.position.set(-6.45, 0, 0);
    kaabaGroup.add(hijr);

    // ---- Maqam Ibrahim (±11 m timur pintu): kubah kristal berbingkai emas ----
    const maqam = new THREE.Group();
    const mBase = new THREE.Mesh(new THREE.CylinderGeometry(1.35, 1.5, 1.1, 8), gold({ roughness: .4 }));
    mBase.position.y = .55;
    const mGlass = new THREE.Mesh(new THREE.SphereGeometry(1.15, 16, 12, 0, Math.PI * 2, 0, Math.PI / 2),
        new THREE.MeshStandardMaterial({ color: 0xd8ecec, transparent: true, opacity: .45, roughness: .1, metalness: .1 }));
    mGlass.position.y = 1.1;
    const mTop = new THREE.Mesh(new THREE.ConeGeometry(.35, .8, 8), gold());
    mTop.position.y = 2.5;
    maqam.add(mBase, mGlass, mTop);
    maqam.position.set(13.5, 0, 4.5);
    staticGroup.add(maqam);

    // ---- Bangunan masjid: cincin arkade 2 lantai + deretan kubah kecil ----
    const arcMat = new THREE.MeshStandardMaterial({ map: arcadeTex(), roughness: .7 });
    const ringSegs = 44;
    const arcadeItems = [], arcadeItems2 = [], domeItems = [], parapetItems = [];
    for (let i = 0; i < ringSegs; i++) {
        const a0 = (i / ringSegs) * Math.PI * 2;
        const gapMasaa = a0 > -0.55 && a0 < 0.55; // bukaan ke arah Mas'a (timur)
        if (gapMasaa && i % 2 === 0) continue;
        const r = 86;
        const x = Math.cos(a0) * r, z = Math.sin(a0) * r;
        const ry = -a0 + Math.PI / 2;
        arcadeItems.push({ p: [x, 6.5, z], r: [0, ry, 0], s: [1, 1, 1] });
        arcadeItems2.push({ p: [x, 17.5, z], r: [0, ry, 0], s: [1, .78, 1] });
        parapetItems.push({ p: [x, 23.3, z], r: [0, ry, 0], s: [1, 1, 1] });
        if (i % 2 === 0) domeItems.push({ p: [x, 24.6, z], r: [0, 0, 0], s: [1, 1, 1] });
    }
    const segLen = 2 * Math.PI * 86 / ringSegs + .8;
    inst(new THREE.BoxGeometry(segLen, 13, 2.4), arcMat, arcadeItems);
    inst(new THREE.BoxGeometry(segLen, 9, 2.4), arcMat, arcadeItems2);
    inst(new THREE.BoxGeometry(segLen, 1.6, 2.8), mat(0xddd5bf), parapetItems);
    inst(new THREE.SphereGeometry(3.1, 12, 8, 0, Math.PI * 2, 0, Math.PI / 2), mat(0xf5f0e1, { roughness: .5 }), domeItems);

    // Blok ekspansi besar (King Abdullah — utara & barat)
    const expTexMat = new THREE.MeshStandardMaterial({ map: arcadeTex(), roughness: .75 });
    const exp1 = new THREE.Mesh(new THREE.BoxGeometry(190, 26, 90), expTexMat);
    exp1.position.set(-60, 12, -150);
    const exp2 = new THREE.Mesh(new THREE.BoxGeometry(90, 24, 150), expTexMat);
    exp2.position.set(-155, 11, -20);
    staticGroup.add(exp1, exp2);

    // ---- 13 menara (dua diapit gerbang utama lebih tinggi) ----
    const minaretAngles = [.9, 1.55, 2.2, 2.85, 3.5, 4.15, 4.8, 5.45];
    minaretAngles.forEach((a, i) => addMinaret(Math.cos(a) * 100, Math.sin(a) * 100, i % 3 === 0 ? 96 : 84));
    addMinaret(-155, -95, 96);
    addMinaret(35, -150, 96);

    // ---- MAS'A: galeri Sa'i 450 m — Safa (tenggara) → Marwah (timur-laut) ----
    const SAFA = new THREE.Vector3(112, 0, 178);
    const MARWAH = new THREE.Vector3(196, 0, -248);
    window.__hajjSai = { SAFA, MARWAH }; // dipakai logika agen
    const saiDir = MARWAH.clone().sub(SAFA);
    const saiLen = saiDir.length();
    const saiAngle = Math.atan2(saiDir.x, saiDir.z);
    const saiCenter = SAFA.clone().add(MARWAH).multiplyScalar(.5);

    const saiGroup = new THREE.Group();
    saiGroup.position.copy(saiCenter);
    saiGroup.rotation.y = saiAngle;
    const saiFloorMat = new THREE.MeshStandardMaterial({ map: marbleTex(40), roughness: .35 });
    const saiFloor = new THREE.Mesh(new THREE.BoxGeometry(24, .8, saiLen + 26), saiFloorMat);
    saiFloor.position.y = -0.4;
    saiGroup.add(saiFloor);
    // dua lantai galeri
    for (const lvlY of [9, 16.5]) {
        const slab = new THREE.Mesh(new THREE.BoxGeometry(24, .9, saiLen + 26), mat(0xe4dcc6));
        slab.position.y = lvlY;
        saiGroup.add(slab);
    }
    // dinding sisi berlubang lengkung
    const saiWallMat = new THREE.MeshStandardMaterial({ map: arcadeTex(), roughness: .7 });
    for (const sx of [-12, 12]) {
        const wall = new THREE.Mesh(new THREE.BoxGeometry(1.6, 17.5, saiLen + 26), saiWallMat);
        wall.position.set(sx, 8.3, 0);
        saiGroup.add(wall);
    }
    // atap melengkung
    const roof = new THREE.Mesh(new THREE.CylinderGeometry(12.6, 12.6, saiLen + 26, 20, 1, true, 0, Math.PI),
        mat(0xded4ba, { side: THREE.DoubleSide }));
    roof.rotation.z = Math.PI / 2;
    roof.rotation.y = Math.PI / 2;
    roof.position.y = 17.4;
    saiGroup.add(roof);
    // zona lampu hijau (lari kecil) di tengah
    const greenMat = new THREE.MeshStandardMaterial({ color: 0x143318, emissive: 0x2fd45a, emissiveIntensity: .8 });
    for (const gz of [-28, 28]) {
        const strip = new THREE.Mesh(new THREE.BoxGeometry(24.4, 1.2, 1.2), greenMat);
        strip.position.set(0, 12, gz);
        saiGroup.add(strip);
    }
    staticGroup.add(saiGroup);

    // Bukit Safa & Marwah: gundukan batu dalam kubah kaca
    for (const [end, name] of [[SAFA, 'safa'], [MARWAH, 'marwah']]) {
        const rocks = new THREE.Group();
        for (let k = 0; k < 7; k++) {
            const rk = new THREE.Mesh(new THREE.DodecahedronGeometry(1.6 + Math.random() * 1.8),
                mat(0x8d7c60, { roughness: 1, flatShading: true }));
            rk.position.set((Math.random() - .5) * 6, .8 + Math.random() * 1.2, (Math.random() - .5) * 6);
            rocks.add(rk);
        }
        const domeG = new THREE.Mesh(new THREE.SphereGeometry(9, 18, 12, 0, Math.PI * 2, 0, Math.PI / 2),
            new THREE.MeshStandardMaterial({ color: 0xcfe4e4, transparent: true, opacity: .3, roughness: .15 }));
        domeG.position.y = .5;
        rocks.add(domeG);
        rocks.position.copy(end);
        staticGroup.add(rocks);
    }

    // ---- Abraj Al-Bait (menara jam) di selatan ----
    const abraj = new THREE.Group();
    const towerTex = buildingTex();
    const towerMat = new THREE.MeshStandardMaterial({ map: towerTex, emissiveMap: towerTex, emissive: 0xffffff, emissiveIntensity: 0, roughness: .85 });
    nightMaterials.push(towerMat);
    const podium = new THREE.Mesh(new THREE.BoxGeometry(150, 42, 90), towerMat);
    podium.position.y = 20;
    const shaft = new THREE.Mesh(new THREE.BoxGeometry(46, 175, 46), towerMat);
    shaft.position.y = 42 + 87;
    const clockBox = new THREE.Mesh(new THREE.BoxGeometry(54, 54, 54), mat(0x274b36, { roughness: .6 }));
    clockBox.position.y = 42 + 175 + 26;
    const clockFaceMat = new THREE.MeshStandardMaterial({ color: 0xf7f5ea, emissive: 0xdaf7c9, emissiveIntensity: 0 });
    nightMaterials.push(clockFaceMat);
    const clockY = 42 + 175 + 26;
    // 4 muka jam: ±Z pakai rotasi X (sumbu silinder → Z), ±X pakai rotasi Z (sumbu → X)
    for (const [px, pz, rx, rz] of [[0, 27.6, Math.PI / 2, 0], [0, -27.6, Math.PI / 2, 0], [27.6, 0, 0, Math.PI / 2], [-27.6, 0, 0, Math.PI / 2]]) {
        const face = new THREE.Mesh(new THREE.CylinderGeometry(21, 21, 1, 24), clockFaceMat);
        face.rotation.x = rx;
        face.rotation.z = rz;
        face.position.set(px, clockY, pz);
        abraj.add(face);
    }
    const spireBase = new THREE.Mesh(new THREE.ConeGeometry(20, 34, 4), mat(0x315c40));
    spireBase.position.y = 42 + 175 + 54 + 15;
    const spire = new THREE.Mesh(new THREE.CylinderGeometry(.9, 2.2, 52, 8), gold());
    spire.position.y = 42 + 175 + 54 + 34 + 24;
    const crescentTop = new THREE.Mesh(new THREE.TorusGeometry(7, 1.1, 8, 20, Math.PI * 1.35), gold());
    crescentTop.position.y = 42 + 175 + 54 + 34 + 52;
    crescentTop.rotation.z = Math.PI * .82;
    abraj.add(podium, shaft, clockBox, spireBase, spire, crescentTop);
    abraj.position.set(20, 0, 330);
    staticGroup.add(abraj);

    // ---- Kota & gunung sekitar ----
    addCityBlocks(260, 560, 260, (x, z) =>
        (z > 220 && Math.abs(x - 20) < 130)                 // area Abraj Al-Bait
        || (x > 70 && x < 250 && z > -300 && z < 220));      // koridor Mas'a (Safa–Marwah)
    addMountains(600, 950, 54, 150);

    // Lampu sorot pelataran
    for (let i = 0; i < 8; i++) {
        const a = i / 8 * Math.PI * 2 + .25;
        addFloodTower(Math.cos(a) * 150, Math.sin(a) * 150, 26);
    }

    // Merpati Masjidil Haram yang ikonik
    spawnBirds(70, [[0, 0], [140, 60], [-140, -60], [60, 200]]);

    scene.fog.near = 400; scene.fog.far = 1600;
    orbit.target.set(0, 10, 0);
    if (cameraMode === 'orbit') camera.position.set(110, 80, 150);
}

// ============================================================
//  SCENE 2: MASJID NABAWI
// ============================================================
function buildNabawi() {
    // Tanah + pelataran marmer sangat luas
    const ground = new THREE.Mesh(new THREE.CircleGeometry(1100, 64),
        new THREE.MeshStandardMaterial({ map: sandTex(70), roughness: 1 }));
    ground.rotation.x = -Math.PI / 2;
    ground.position.y = -1.2;
    staticGroup.add(ground);
    const plaza = new THREE.Mesh(new THREE.BoxGeometry(560, .7, 480),
        new THREE.MeshStandardMaterial({ map: marbleTex(80), roughness: .45 }));
    plaza.position.y = -0.85;
    staticGroup.add(plaza);

    const arcMat = new THREE.MeshStandardMaterial({ map: arcadeTex(), roughness: .7 });

    // ---- Bangunan masjid: bagian lama (selatan) + perluasan (utara) ----
    // Bagian lama (selatan, z>0) — tempat Kubah Hijau di pojok tenggara
    const oldSec = new THREE.Mesh(new THREE.BoxGeometry(210, 15, 105), arcMat);
    oldSec.position.set(0, 7.5, 88);
    staticGroup.add(oldSec);
    // Perluasan utara dengan 27 kubah geser
    const newSec = new THREE.Mesh(new THREE.BoxGeometry(210, 16, 160), arcMat);
    newSec.position.set(0, 8, -55);
    staticGroup.add(newSec);
    // parapet keliling
    const parapet = new THREE.Mesh(new THREE.BoxGeometry(214, 2, 270), mat(0xddd5bf));
    parapet.position.set(0, 16.8, 0);
    staticGroup.add(parapet);

    // 27 kubah geser (grid 3×9 di atap perluasan)
    const slideDomes = [];
    for (let r = 0; r < 3; r++)
        for (let c = 0; c < 9; c++)
            slideDomes.push({ p: [-84 + c * 21, 17.6, -110 + r * 38], s: [1, 1, 1] });
    inst(new THREE.SphereGeometry(6.2, 14, 10, 0, Math.PI * 2, 0, Math.PI / 2),
        mat(0xf3ede0, { roughness: .45 }), slideDomes);
    // dasar rel persegi tiap kubah
    inst(new THREE.BoxGeometry(14.5, 1.4, 14.5), mat(0xe6dfc9),
        slideDomes.map(d => ({ p: [d.p[0], 17.1, d.p[2]] })));

    // ---- KUBAH HIJAU (pojok tenggara bagian lama) + kubah perak kecil ----
    const gd = new THREE.Group();
    const drum = new THREE.Mesh(new THREE.CylinderGeometry(8.4, 8.9, 5.5, 16), mat(0xefe8d5));
    drum.position.y = 15 + 2.7;
    const domeGreen = new THREE.Mesh(new THREE.SphereGeometry(8.2, 22, 16, 0, Math.PI * 2, 0, Math.PI * .58),
        mat(0x0e6b39, { roughness: .35 }));
    domeGreen.position.y = 15 + 5.5;
    const finial = new THREE.Mesh(new THREE.CylinderGeometry(.35, .55, 5, 8), gold());
    finial.position.y = 15 + 5.5 + 9.5;
    const cres = new THREE.Mesh(new THREE.TorusGeometry(1.5, .28, 6, 16, Math.PI * 1.4), gold());
    cres.position.y = 15 + 5.5 + 13;
    cres.rotation.z = Math.PI * .8;
    gd.add(drum, domeGreen, finial, cres);
    gd.position.set(78, 0, 120);
    staticGroup.add(gd);
    // kubah perak (Bab al-Salam area)
    const silverDome = new THREE.Mesh(new THREE.SphereGeometry(5.2, 16, 10, 0, Math.PI * 2, 0, Math.PI / 2),
        mat(0xc9ccd2, { metalness: .5, roughness: .35 }));
    silverDome.position.set(52, 15, 120);
    staticGroup.add(silverDome);

    // Penanda Raudhah (zona hijau di lantai pelataran sisi tenggara — simbolik)
    const raudhah = new THREE.Mesh(new THREE.BoxGeometry(24, .25, 14),
        new THREE.MeshStandardMaterial({ color: 0x1e7a45, emissive: 0x1e7a45, emissiveIntensity: .3 }));
    raudhah.position.set(70, 0, 150);
    staticGroup.add(raudhah);

    // ---- 10 menara ----
    const mPos = [[-108, 145], [108, 145], [-108, 0], [108, 0], [-108, -140], [108, -140], [-40, -140], [40, -140], [-40, 145], [40, 145]];
    mPos.forEach(([x, z], i) => addMinaret(x, z, i < 6 ? 72 : 62, 'nabawi'));

    // ---- Payung raksasa PERSEGI di pelataran (khas Nabawi) ----
    const umbSpots = [];
    for (const zone of [[-260, -120], [130, 250]]) // pelataran barat & timur? → utara & selatan plaza
        for (let x = -230; x <= 230; x += 46)
            for (let z = zone[0]; z <= zone[1]; z += 46)
                if (Math.abs(x) > 118 || z < -150 || z > 155) umbSpots.push([x, z]);
    // tiang
    inst(new THREE.CylinderGeometry(.7, .9, 14, 8), mat(0xf3efe4, { roughness: .5 }),
        umbSpots.map(([x, z]) => ({ p: [x, 7, z] })));
    // kanopi persegi (piramida sangat landai, diputar 45° agar sisi lurus)
    inst(new THREE.ConeGeometry(30, 3.2, 4), mat(0xf6f1e2, { roughness: .55, side: THREE.DoubleSide }),
        umbSpots.map(([x, z]) => ({ p: [x, 14.6, z], r: [0, Math.PI / 4, 0], s: [1, 1, 1] })));
    // lampu di bawah payung (malam)
    const umbLampMat = new THREE.MeshStandardMaterial({ color: 0x555544, emissive: 0xffe8b0, emissiveIntensity: 0 });
    nightMaterials.push(umbLampMat);
    inst(new THREE.SphereGeometry(.55, 6, 6), umbLampMat,
        umbSpots.map(([x, z]) => ({ p: [x, 12.6, z] })));

    // ---- Pohon kurma di tepi pelataran ----
    const palms = [];
    for (let x = -270; x <= 270; x += 34) { palms.push([x, 232], [x, -252]); }
    for (let z = -240; z <= 220; z += 38) { palms.push([272, z], [-272, z]); }
    addPalms(palms);

    // Kota & pegunungan Madinah (lebih landai)
    addCityBlocks(380, 640, 220, (x, z) => Math.abs(x) < 300 && Math.abs(z) < 260);
    addMountains(700, 980, 40, 90);
    for (let i = 0; i < 6; i++) addFloodTower(-250 + i * 100, 260, 22);

    // Merpati pelataran Nabawi
    spawnBirds(50, [[0, 200], [0, -200], [180, 0], [-180, 0]]);

    scene.fog.near = 420; scene.fog.far = 1700;
    orbit.target.set(0, 10, 20);
    if (cameraMode === 'orbit') camera.position.set(210, 130, 300);
}

// ============================================================
//  SCENE 3: MANASIK — ARAFAH · MUZDALIFAH · MINA · JAMARAT
// ============================================================
function buildManasik() {
    const ground = new THREE.Mesh(new THREE.CircleGeometry(1200, 64),
        new THREE.MeshStandardMaterial({ map: sandTex(90), roughness: 1 }));
    ground.rotation.x = -Math.PI / 2;
    ground.position.y = -1;
    staticGroup.add(ground);

    // Jalan raya penghubung (Arafah barat → Muzdalifah → Mina timur)
    const roadMat = mat(0x54504a, { roughness: .95 });
    const road = new THREE.Mesh(new THREE.BoxGeometry(760, .3, 14), roadMat);
    road.position.set(0, -0.3, 10);
    staticGroup.add(road);
    const road2 = new THREE.Mesh(new THREE.BoxGeometry(14, .3, 220), roadMat);
    road2.position.set(-160, -0.3, -90);
    road2.rotation.y = .5;
    staticGroup.add(road2);

    // ---- ARAFAH (barat): Jabal Rahmah + tugu + Masjid Namirah + pepohonan ----
    // Jabal Rahmah: tumpukan granit
    const jabal = new THREE.Group();
    for (let k = 0; k < 24; k++) {
        const rock = new THREE.Mesh(new THREE.DodecahedronGeometry(7 + Math.random() * 9),
            mat(0x87715a, { roughness: 1, flatShading: true }));
        const a = Math.random() * Math.PI * 2, rr = Math.random() * 22;
        rock.position.set(Math.cos(a) * rr, Math.random() * 12, Math.sin(a) * rr);
        rock.scale.y = .55 + Math.random() * .4;
        jabal.add(rock);
    }
    const obelisk = new THREE.Mesh(new THREE.BoxGeometry(2.4, 8, 2.4), mat(0xf5f2ea, { roughness: .4 }));
    obelisk.position.y = 20;
    jabal.add(obelisk);
    jabal.position.set(-300, 0, -60);
    staticGroup.add(jabal);
    // jalur pendakian putih melingkar
    const path = new THREE.Mesh(new THREE.TorusGeometry(26, 1.2, 6, 40, Math.PI * 1.5), mat(0xd8d2c4));
    path.rotation.x = -Math.PI / 2;
    path.position.set(-300, .4, -60);
    staticGroup.add(path);

    // Masjid Namirah (tepi barat Arafah): bangunan panjang putih + 2 pasang menara
    const namirah = new THREE.Group();
    const nBody = new THREE.Mesh(new THREE.BoxGeometry(70, 9, 30),
        new THREE.MeshStandardMaterial({ map: arcadeTex(), roughness: .7 }));
    nBody.position.y = 4.5;
    const nBand = new THREE.Mesh(new THREE.BoxGeometry(70.5, 1.4, 30.5), mat(0x0e6b39));
    nBand.position.y = 9.6;
    namirah.add(nBody, nBand);
    namirah.position.set(-350, 0, 90);
    staticGroup.add(namirah);
    addMinaret(-383, 78, 34); addMinaret(-317, 78, 34);
    addMinaret(-383, 102, 34); addMinaret(-317, 102, 34);

    // Pepohonan Arafah (pohon neem 'Sukarno')
    const treeSpots = [];
    for (let i = 0; i < 130; i++) {
        treeSpots.push([-420 + Math.random() * 240, -170 + Math.random() * 300]);
    }
    inst(new THREE.CylinderGeometry(.3, .45, 4, 6), mat(0x6d5335, { roughness: 1 }),
        treeSpots.map(([x, z]) => ({ p: [x, 2, z] })));
    inst(new THREE.SphereGeometry(3.2, 8, 6), mat(0x4a7c3a, { roughness: .95 }),
        treeSpots.map(([x, z]) => ({ p: [x, 5.4, z], s: [1, .8 + Math.random() * .4, 1] })));

    // Tenda putih Arafah tersebar
    const arafahTents = [];
    for (let i = 0; i < 160; i++)
        arafahTents.push({ p: [-430 + Math.random() * 220, 1.4, -180 + Math.random() * 320], r: [0, Math.random() * 3, 0] });
    inst(new THREE.BoxGeometry(6, 2.8, 5), mat(0xf6f4ec, { roughness: .8 }), arafahTents);

    // ---- MUZDALIFAH (tengah-utara): dataran kerikil terbuka + Masjid Masy'aril Haram ----
    const gravel = new THREE.Mesh(new THREE.CylinderGeometry(80, 80, .3, 24),
        mat(0xa89a7f, { roughness: 1 }));
    gravel.position.set(-60, -0.2, -160);
    staticGroup.add(gravel);
    const mashar = new THREE.Group();
    const mbody = new THREE.Mesh(new THREE.BoxGeometry(26, 7, 18), new THREE.MeshStandardMaterial({ map: arcadeTex(), roughness: .7 }));
    mbody.position.y = 3.5;
    const mdome = new THREE.Mesh(new THREE.SphereGeometry(6, 14, 10, 0, Math.PI * 2, 0, Math.PI / 2), mat(0xe8e0ca));
    mdome.position.y = 7;
    mashar.add(mbody, mdome);
    mashar.position.set(-60, 0, -160);
    staticGroup.add(mashar);
    addMinaret(-75, -170, 26); addMinaret(-45, -170, 26);
    for (let i = 0; i < 5; i++) addFloodTower(-110 + i * 26, -120, 20);
    // bebatuan kerikil
    const rocks = [];
    for (let i = 0; i < 60; i++) {
        const a = Math.random() * Math.PI * 2, r = Math.random() * 70;
        rocks.push({ p: [-60 + Math.cos(a) * r, .5, -160 + Math.sin(a) * r], s: [1, .7, 1], r: [0, Math.random() * 3, 0] });
    }
    inst(new THREE.DodecahedronGeometry(1.1), mat(0x93835f, { roughness: 1, flatShading: true }), rocks);

    // ---- JAMARAT (tengah): jembatan multi-lantai + 3 pilar elips ----
    // urutan dari arah Mina (timur): Ula → Wustha → Aqabah (paling barat, dekat Makkah)
    const JX = [-55, 0, 55]; // posisi x pilar (Aqabah, Wustha, Ula) relatif pusat jamarat
    const jz = 60;
    const bridgeMat = mat(0xcfc7b2, { roughness: .8 });
    for (const lvlY of [7, 14]) {
        const slab = new THREE.Mesh(new THREE.BoxGeometry(260, 1.6, 42), bridgeMat);
        slab.position.set(0, lvlY, jz);
        staticGroup.add(slab);
    }
    // ramp dua ujung
    for (const [rx, rr] of [[-158, .12], [158, -.12]]) {
        const ramp = new THREE.Mesh(new THREE.BoxGeometry(62, 1.6, 42), bridgeMat);
        ramp.position.set(rx, 3.8, jz);
        ramp.rotation.z = rr;
        staticGroup.add(ramp);
    }
    // dinding pengaman
    for (const wz of [jz - 20, jz + 20]) {
        const rail = new THREE.Mesh(new THREE.BoxGeometry(260, 1.4, .6), mat(0xb9b198));
        rail.position.set(0, 8.4, wz);
        staticGroup.add(rail.clone());
        rail.position.y = 15.4;
        staticGroup.add(rail);
    }
    // pilar elips + kolam penampung + kanopi putih
    for (const px of JX) {
        const pillar = new THREE.Mesh(new THREE.CylinderGeometry(2.2, 2.2, 22, 18), mat(0x8f8678, { roughness: .9 }));
        pillar.scale.x = 3.4;
        pillar.position.set(px, 11, jz);
        staticGroup.add(pillar);
        const basin = new THREE.Mesh(new THREE.CylinderGeometry(10, 10, 2.2, 20, 1, true), mat(0x6e675c, { side: THREE.DoubleSide }));
        basin.scale.x = 1.7;
        basin.position.set(px, 1.1, jz);
        staticGroup.add(basin);
        const canopy = new THREE.Mesh(new THREE.ConeGeometry(19, 7, 10), mat(0xf4f0e3, { roughness: .6, side: THREE.DoubleSide }));
        canopy.position.set(px, 22, jz);
        staticGroup.add(canopy);
        const cpole = new THREE.Mesh(new THREE.CylinderGeometry(.5, .5, 8, 6), mat(0xcccccc));
        cpole.position.set(px, 17, jz);
        staticGroup.add(cpole);
    }

    // ---- MINA (timur): kota tenda putih rapi dalam blok + jalan ----
    const tentBase = [], tentRoof = [];
    for (let gx = 0; gx < 26; gx++) {
        for (let gz = 0; gz < 18; gz++) {
            if (gx % 6 === 5 || gz % 5 === 4) continue; // gang antar blok
            const x = 150 + gx * 10.5, z = -120 + gz * 10.5;
            tentBase.push({ p: [x, 1.5, z] });
            tentRoof.push({ p: [x, 3.55, z], r: [0, Math.PI / 4, 0] });
        }
    }
    inst(new THREE.BoxGeometry(8, 3, 8), mat(0xf7f5ee, { roughness: .75 }), tentBase);
    inst(new THREE.ConeGeometry(6, 2.6, 4), mat(0xefede2, { roughness: .7 }), tentRoof);
    for (let i = 0; i < 8; i++) addFloodTower(150 + i * 34, -132, 22);

    // ---- Punggung gunung mengapit lembah Mina (utara & selatan) ----
    const ridge = [];
    for (let i = 0; i < 20; i++) {
        ridge.push({ p: [80 + i * 22, -5, -185 - Math.random() * 40], s: [1.4, .9 + Math.random() * .8, 1.3], r: [0, Math.random() * 3, 0], c: 0x7d6950 });
        ridge.push({ p: [80 + i * 22, -5, 105 + Math.random() * 40], s: [1.4, .8 + Math.random() * .8, 1.3], r: [0, Math.random() * 3, 0], c: 0x776448 });
    }
    inst(new THREE.ConeGeometry(30, 44, 7), mat(0x7d6950, { roughness: 1, flatShading: true }), ridge);
    addMountains(700, 1050, 44, 120);

    scene.fog.near = 500; scene.fog.far = 1900;
    orbit.target.set(-40, 6, 0);
    if (cameraMode === 'orbit') camera.position.set(260, 200, 320);
}

// ============================================================
//  CROWD — jamaah dua bagian (badan + kepala), warna bervariasi
// ============================================================
const SKIN_TONES = [0xf1c6a7, 0xd9a066, 0xb87f4e, 0x8a5a33, 0x6b4226, 0xe8b48a];

function clothingColor() {
    const r = Math.random();
    if (sceneKey === 'nabawi') {
        if (r < .35) return 0xf5f2ea;           // thobe putih
        if (r < .6) return 0x17151a;            // abaya hitam
        return [0x7d5a7a, 0x4a6d8c, 0x8c5a4a, 0x5a8c6d, 0xc2b280][(Math.random() * 5) | 0];
    }
    // haram & manasik: mayoritas ihram putih
    if (r < .72) return 0xf7f5ee;               // ihram
    if (r < .9) return 0x17151a;                // abaya
    return [0x6d8ca8, 0x8c6d5a, 0xa89a5a][(Math.random() * 3) | 0];
}

function buildCrowd(n) {
    if (bodyMesh) { scene.remove(bodyMesh); bodyMesh.geometry.dispose(); bodyMesh.material.dispose(); }
    if (headMesh) { scene.remove(headMesh); headMesh.geometry.dispose(); headMesh.material.dispose(); }
    crowdCount = n;
    bodyMesh = new THREE.InstancedMesh(new THREE.CapsuleGeometry(0.3, 1.0, 3, 6), new THREE.MeshLambertMaterial(), n);
    headMesh = new THREE.InstancedMesh(new THREE.SphereGeometry(0.155, 8, 7), new THREE.MeshLambertMaterial(), n);
    bodyMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
    headMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
    scene.add(bodyMesh, headMesh);
    rebuildAgents();
}

function rebuildAgents() {
    agents = [];
    if (!bodyMesh) return;
    const color = new THREE.Color();
    for (let i = 0; i < crowdCount; i++) {
        const a = makeAgent(i);
        a.scale = .9 + Math.random() * .2;
        agents.push(a);
        const clothing = clothingColor();
        color.setHex(clothing);
        bodyMesh.setColorAt(i, color);
        // jamaah perempuan berhijab: warna kepala = warna pakaian (kerudung menutup kepala)
        const isIhramWhite = clothing === 0xf7f5ee || clothing === 0xf5f2ea;
        if (!isIhramWhite && Math.random() < .5) color.setHex(clothing);
        else color.setHex(SKIN_TONES[(Math.random() * SKIN_TONES.length) | 0]);
        headMesh.setColorAt(i, color);
    }
    if (bodyMesh.instanceColor) bodyMesh.instanceColor.needsUpdate = true;
    if (headMesh.instanceColor) headMesh.instanceColor.needsUpdate = true;
    updateCrowd(0);
}

function makeAgent(i) {
    if (sceneKey === 'haram') {
        const sai = window.__hajjSai;
        if (i % 10 < 7 || !sai) {
            const radius = 11 + Math.pow(Math.random(), 1.4) * 48; // lebih padat dekat Ka'bah
            return {
                kind: 'tawaf', radius,
                angle: Math.random() * Math.PI * 2,
                speed: (0.75 + Math.random() * 0.7) / Math.sqrt(radius) * 2.2,
                bob: Math.random() * Math.PI * 2
            };
        }
        return {
            kind: 'sai',
            t: Math.random(),
            lane: (Math.random() - .5) * 18,
            dir: Math.random() < .5 ? 1 : -1,
            speed: (2.8 + Math.random() * 2.6),
            bob: Math.random() * Math.PI * 2
        };
    }
    if (sceneKey === 'nabawi') {
        const avoid = { x0: -107, x1: 107, z0: -137, z1: 142 }; // bangunan masjid
        let x, z;
        do {
            x = -250 + Math.random() * 500;
            z = -240 + Math.random() * 460;
        } while (x > avoid.x0 && x < avoid.x1 && z > avoid.z0 && z < avoid.z1);
        return {
            kind: 'wander',
            x, z, tx: x, tz: z,
            speed: 1.4 + Math.random() * 1.8, bob: Math.random() * Math.PI * 2,
            bounds: [-260, 260, -250, 230],
            avoid
        };
    }
    // manasik
    const zone = i % 4;
    if (zone === 0) { // wukuf di Arafah — duduk berdoa dekat Jabal Rahmah
        const a = Math.random() * Math.PI * 2, r = 26 + Math.random() * 90;
        return { kind: 'idle', x: -300 + Math.cos(a) * r, z: -60 + Math.sin(a) * r, sit: Math.random() < .6, bob: Math.random() * Math.PI * 2 };
    }
    if (zone === 1) { // melempar jumrah — mengitari pilar di lantai dasar
        const pillar = (Math.random() * 3) | 0;
        return {
            kind: 'jumrah',
            px: [-55, 0, 55][pillar], pz: 60,
            angle: Math.random() * Math.PI * 2,
            r: 12 + Math.random() * 26,
            speed: .25 + Math.random() * .45,
            bob: Math.random() * Math.PI * 2
        };
    }
    if (zone === 2) { // lalu-lalang di Mina
        return {
            kind: 'wander',
            x: 150 + Math.random() * 250, z: -120 + Math.random() * 180,
            tx: 150 + Math.random() * 250, tz: -120 + Math.random() * 180,
            speed: 1.1 + Math.random() * 1.6, bob: Math.random() * Math.PI * 2,
            bounds: [145, 410, -125, 65]
        };
    }
    // mabit di Muzdalifah — duduk/berbaring di kerikil
    const a2 = Math.random() * Math.PI * 2, r2 = Math.random() * 70;
    return { kind: 'idle', x: -60 + Math.cos(a2) * r2, z: -160 + Math.sin(a2) * r2, sit: true, bob: Math.random() * Math.PI * 2 };
}

const _v1 = new THREE.Vector3();
function updateCrowd(dt) {
    const t = clock ? clock.elapsedTime : 0;
    const sai = window.__hajjSai;
    for (let i = 0; i < agents.length; i++) {
        const a = agents[i];
        let x, z, ry = 0;
        switch (a.kind) {
            case 'tawaf':
                a.angle -= a.speed * dt; // berlawanan arah jarum jam dari atas
                x = Math.cos(a.angle) * a.radius;
                z = Math.sin(a.angle) * a.radius;
                ry = -a.angle;
                break;
            case 'sai': {
                a.t += a.dir * a.speed * dt / 430;
                if (a.t > 1) { a.t = 1; a.dir = -1; }
                if (a.t < 0) { a.t = 0; a.dir = 1; }
                _v1.lerpVectors(sai.SAFA, sai.MARWAH, a.t);
                const dirA = Math.atan2(sai.MARWAH.x - sai.SAFA.x, sai.MARWAH.z - sai.SAFA.z);
                x = _v1.x + Math.cos(dirA) * a.lane;
                z = _v1.z - Math.sin(dirA) * a.lane;
                ry = a.dir > 0 ? dirA : dirA + Math.PI;
                break;
            }
            case 'wander': {
                const dx = a.tx - a.x, dz = a.tz - a.z;
                const d = Math.hypot(dx, dz);
                if (d < 2) {
                    do {
                        a.tx = a.bounds[0] + Math.random() * (a.bounds[1] - a.bounds[0]);
                        a.tz = a.bounds[2] + Math.random() * (a.bounds[3] - a.bounds[2]);
                    } while (a.avoid && a.tx > a.avoid.x0 && a.tx < a.avoid.x1 && a.tz > a.avoid.z0 && a.tz < a.avoid.z1);
                } else {
                    a.x += (dx / d) * a.speed * dt;
                    a.z += (dz / d) * a.speed * dt;
                    ry = Math.atan2(dx, dz);
                }
                x = a.x; z = a.z;
                break;
            }
            case 'jumrah':
                a.angle += a.speed * dt;
                x = a.px + Math.cos(a.angle) * a.r * 1.9;
                z = a.pz + Math.sin(a.angle) * a.r * .8;
                ry = Math.atan2(a.px - x, a.pz - z);
                break;
            default: // idle
                x = a.x; z = a.z;
                break;
        }
        const s = a.scale;
        const sitScale = a.sit ? .62 : 1;
        const bobY = (a.kind === 'idle' ? .02 : .085) * Math.abs(Math.sin(t * 4.2 + a.bob));
        // badan
        dummy.position.set(x, (0.82 * sitScale + bobY) * s, z);
        dummy.rotation.set(0, ry, 0);
        dummy.scale.set(s, s * sitScale, s);
        dummy.updateMatrix();
        bodyMesh.setMatrixAt(i, dummy.matrix);
        // kepala
        dummy.position.set(x, (1.52 * sitScale + bobY) * s, z);
        dummy.scale.set(s, s, s);
        dummy.updateMatrix();
        headMesh.setMatrixAt(i, dummy.matrix);
    }
    bodyMesh.instanceMatrix.needsUpdate = true;
    headMesh.instanceMatrix.needsUpdate = true;
}

// ---------- Siang / malam ----------
export function setTimeOfDay(t) { timeOfDay = t; applyTime(t); }

function applyTime(t) {
    const dayness = Math.max(0, Math.sin(((t - 6) / 12) * Math.PI));
    const sunsetness = Math.max(0, 1 - Math.abs(dayness - .18) * 6); // jelang terbit/terbenam
    const angle = ((t - 6) / 12) * Math.PI;

    const sunPos = new THREE.Vector3(Math.cos(angle) * 300, Math.sin(angle) * 320, 120);
    sun.position.copy(sunPos);
    sun.intensity = 0.1 + dayness * 2.0;
    sun.color.setHSL(0.09, 0.6, 0.52 + dayness * 0.4);
    hemi.intensity = 0.1 + dayness * 0.8;
    ambient.intensity = 0.1 + dayness * 0.16;

    // piringan matahari & bulan
    sunDisc.position.copy(sunPos).normalize().multiplyScalar(1850);
    sunDisc.visible = sunPos.y > -20;
    moonDisc.position.copy(sunPos).multiplyScalar(-1).normalize().multiplyScalar(1800);
    moonDisc.visible = sunPos.y < 40;

    // langit gradient: malam → jingga senja → biru siang
    const horizon = SKY_NIGHT.clone().lerp(SKY_DAY, dayness).lerp(SKY_SET, sunsetness * .75);
    const zenith = new THREE.Color(0x03050c).lerp(new THREE.Color(0x3d8bd6), dayness)
        .lerp(new THREE.Color(0x6a5a8c), sunsetness * .35);
    if (skyDome) {
        skyDome.material.uniforms.horizon.value.copy(horizon);
        skyDome.material.uniforms.top.value.copy(zenith);
    }
    scene.fog.color.copy(horizon);
    if (cloudMesh) cloudMesh.material.opacity = .2 + dayness * .6;

    // bintang
    stars.material.opacity = Math.max(0, 1 - dayness * 2.2);

    // lampu gedung/menara/payung menyala saat gelap
    const night = Math.max(0, 1 - dayness * 1.6);
    for (const m of nightMaterials) m.emissiveIntensity = night * .85;
}

// ---------- Mode kamera ----------
export function setCameraMode(mode) {
    cameraMode = mode;
    plc.unlock?.();
    orbit.enabled = (mode === 'orbit' || mode === 'bird');
    if (mode === 'bird') {
        camera.position.set(0, 520, 1);
        orbit.target.set(0, 0, 0);
    } else if (mode === 'orbit') {
        camera.position.set(110, 80, 150);
        orbit.target.set(0, 10, 0);
    } else if (mode === 'fp') {
        camera.position.set(35, 1.7, 35);
        plc.lock();
    } else if (mode === 'tp') {
        followIdx = (Math.random() * agents.length) | 0;
    }
}

function onKey(e) { keys[e.code] = true; }
function onKeyUp(e) { keys[e.code] = false; }

function updateCamera(dt) {
    if (cameraMode === 'fp' && plc.isLocked) {
        const speed = (keys['ShiftLeft'] ? 26 : 10) * dt;
        const fwd = new THREE.Vector3();
        camera.getWorldDirection(fwd); fwd.y = 0; fwd.normalize();
        const right = new THREE.Vector3().crossVectors(fwd, new THREE.Vector3(0, 1, 0));
        if (keys['KeyW'] || keys['ArrowUp']) camera.position.addScaledVector(fwd, speed);
        if (keys['KeyS'] || keys['ArrowDown']) camera.position.addScaledVector(fwd, -speed);
        if (keys['KeyD'] || keys['ArrowRight']) camera.position.addScaledVector(right, speed);
        if (keys['KeyA'] || keys['ArrowLeft']) camera.position.addScaledVector(right, -speed);
        camera.position.y = 1.7;
    } else if (cameraMode === 'tp' && agents.length > 0) {
        bodyMesh.getMatrixAt(followIdx % agents.length, dummy.matrix);
        const pos = new THREE.Vector3().setFromMatrixPosition(dummy.matrix);
        const behind = pos.clone().add(new THREE.Vector3(-9, 6.5, -9));
        camera.position.lerp(behind, 0.06);
        camera.lookAt(pos.x, pos.y + 1.2, pos.z);
    }
}

// ---------- API ----------
export function setScene(key) { buildScene(key); }
export function setCrowd(n) { buildCrowd(Math.max(1, Math.min(20000, n | 0))); }

/** Kecepatan simulasi crowd (0.25–4x) */
export function setSimSpeed(x) { simSpeed = Math.max(.25, Math.min(4, x)); }
export function setPaused(p) { paused = !!p; }

/** Siklus siang-malam otomatis. speed = jam simulasi per detik nyata. */
export function setAutoTime(on, speed = .35) { autoTime = !!on; autoTimeSpeed = speed; }

/** Fokuskan kamera ke lokasi penting (lihat POIS). */
export function focusOn(key) {
    const poi = POIS[sceneKey]?.[key];
    if (!poi) return false;
    if (cameraMode === 'fp' || cameraMode === 'tp') setCameraMode('orbit');
    const target = new THREE.Vector3(...poi.t);
    const offset = new THREE.Vector3(.72, .62, .72).normalize().multiplyScalar(poi.d);
    camGoal = { target, pos: target.clone().add(offset) };
    return true;
}

export function getPois() { return Object.keys(POIS[sceneKey] || {}); }

export function resetCamera() { camGoal = null; setCameraMode('orbit'); }

/** Kualitas render: bayangan matahari + resolusi. 'high' = bayangan nyala. */
export function setQuality(level) {
    shadowsOn = level === 'high';
    renderer.shadowMap.enabled = shadowsOn;
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, shadowsOn ? 2 : 1.5));
    sun.castShadow = shadowsOn;
    if (shadowsOn) {
        sun.shadow.mapSize.set(2048, 2048);
        const c = sun.shadow.camera;
        c.left = -380; c.right = 380; c.top = 380; c.bottom = -380;
        c.near = 50; c.far = 1300;
        sun.shadow.bias = -0.0006;
        c.updateProjectionMatrix();
        scene.add(sun.target);
    }
    scene.traverse(o => {
        if (o.isMesh || o.isInstancedMesh) {
            if (o === skyDome || o === cloudMesh || o === sunDisc || o === moonDisc || o === stars) return;
            o.castShadow = shadowsOn && o !== bodyMesh && o !== headMesh && o !== birdMesh;
            o.receiveShadow = shadowsOn;
            const mats = Array.isArray(o.material) ? o.material : [o.material];
            mats.forEach(m => { if (m) m.needsUpdate = true; });
        }
    });
    return shadowsOn;
}

export function getStats() {
    return {
        fps: Math.round(fps),
        agents: agents.length,
        drawCalls: renderer ? renderer.info.render.calls : 0,
        triangles: renderer ? renderer.info.render.triangles : 0,
        time: Math.round(timeOfDay * 10) / 10,
        paused,
        shadows: shadowsOn
    };
}

export function dispose() {
    disposed = true;
    if (renderer) {
        renderer.setAnimationLoop(null);
        renderer.dispose();
    }
    window.removeEventListener('keydown', onKey);
    window.removeEventListener('keyup', onKeyUp);
    agents = [];
    bodyMesh = null;
    headMesh = null;
    scene = null;
}

// ---------- Loop ----------
function animate() {
    if (disposed || !scene) return;
    const dt = Math.min(clock.getDelta(), 0.05);
    const t = clock.elapsedTime;

    // siklus siang-malam otomatis
    if (autoTime && !paused) {
        timeOfDay = (timeOfDay + dt * autoTimeSpeed) % 24;
        applyTime(timeOfDay);
    }

    if (!paused) {
        simTime += dt * simSpeed;
        updateCrowd(dt * simSpeed);
        updateBirds(simTime);
    }
    if (cloudMesh) cloudMesh.rotation.y += dt * 0.0035; // awan berarak pelan

    // transisi halus fokus kamera (POI)
    if (camGoal && (cameraMode === 'orbit' || cameraMode === 'bird')) {
        orbit.target.lerp(camGoal.target, .07);
        camera.position.lerp(camGoal.pos, .07);
        if (camera.position.distanceTo(camGoal.pos) < 1.2) camGoal = null;
    }

    updateCamera(dt);
    if (orbit.enabled) orbit.update();
    renderer.render(scene, camera);

    frames++;
    const now = performance.now();
    if (now - lastFpsTime > 500) {
        fps = frames * 1000 / (now - lastFpsTime);
        frames = 0; lastFpsTime = now;
    }
}
