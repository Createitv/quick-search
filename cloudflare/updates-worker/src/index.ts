export interface Env {
  UPDATES: R2Bucket;
}

const DefaultKey = "latest.json";

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    const key = decodeURIComponent(url.pathname.replace(/^\/+/, "")) || DefaultKey;
    const object = await env.UPDATES.get(key);
    if (object === null) {
      return new Response("Not found", { status: 404 });
    }

    const headers = new Headers();
    object.writeHttpMetadata(headers);
    headers.set("etag", object.httpEtag);
    headers.set("access-control-allow-origin", "*");
    headers.set(
      "cache-control",
      key === DefaultKey
        ? "no-cache"
        : "public, max-age=31536000, immutable");

    return new Response(object.body, { headers });
  },
};
