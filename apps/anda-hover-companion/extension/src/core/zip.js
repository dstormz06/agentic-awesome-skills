// Reads the entries of a .zip (an .xlsx is one) with the browser's own DecompressionStream. No library, no zip64, no encryption.
// Hostile files fail with a plain message: sizes are checked before and while decompressing, so a zip bomb cannot exhaust memory.
export class ZipError extends Error {}

const u16 = (b, o) => b[o] | (b[o + 1] << 8);
const u32 = (b, o) => (b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24)) >>> 0;

export function listEntries(bytes) {
  let eocd = -1;
  for (let i = bytes.length - 22; i >= Math.max(0, bytes.length - 65557); i--) if (u32(bytes, i) === 0x06054b50) { eocd = i; break; }
  if (eocd < 0) throw new ZipError('not a zip');
  const count = u16(bytes, eocd + 10), dirSize = u32(bytes, eocd + 12), dirOffset = u32(bytes, eocd + 16);
  if (count === 0xffff || dirOffset === 0xffffffff || dirSize === 0xffffffff) throw new ZipError('zip64');
  const entries = new Map();
  let p = dirOffset;
  const dec = new TextDecoder();
  for (let k = 0; k < count; k++) {
    if (p + 46 > bytes.length || u32(bytes, p) !== 0x02014b50) throw new ZipError('bad directory');
    const flags = u16(bytes, p + 8), method = u16(bytes, p + 10), csize = u32(bytes, p + 20), usize = u32(bytes, p + 24);
    const nlen = u16(bytes, p + 28), elen = u16(bytes, p + 30), clen = u16(bytes, p + 32), local = u32(bytes, p + 42);
    const name = dec.decode(bytes.subarray(p + 46, p + 46 + nlen));
    if (flags & 1) throw new ZipError('encrypted');
    entries.set(name, { name, method, csize, usize, local });
    p += 46 + nlen + elen + clen;
  }
  return entries;
}

export async function readEntry(bytes, entry, maxBytes) {
  if (entry.usize > maxBytes) throw new ZipError('too large');
  const p = entry.local;
  if (u32(bytes, p) !== 0x04034b50) throw new ZipError('bad entry');
  const start = p + 30 + u16(bytes, p + 26) + u16(bytes, p + 28);
  const raw = bytes.subarray(start, start + entry.csize);
  if (entry.method === 0) return raw;
  if (entry.method !== 8) throw new ZipError('unsupported compression');
  const stream = new Blob([raw]).stream().pipeThrough(new DecompressionStream('deflate-raw'));
  const reader = stream.getReader();
  const chunks = [];
  let total = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    total += value.length;
    if (total > Math.min(maxBytes, entry.usize + 1024)) { await reader.cancel(); throw new ZipError('expands beyond its declared size'); }
    chunks.push(value);
  }
  const out = new Uint8Array(total);
  let o = 0;
  for (const c of chunks) { out.set(c, o); o += c.length; }
  return out;
}
