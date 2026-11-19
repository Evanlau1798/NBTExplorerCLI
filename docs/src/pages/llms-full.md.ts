import type { APIRoute } from 'astro';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

export const prerender = true;

export const GET: APIRoute = () => {
  const content = readFileSync(resolve(process.cwd(), 'public/llms.txt'), 'utf-8');
  return new Response(content, {
    headers: {
      'Content-Type': 'text/markdown; charset=utf-8',
    },
  });
};
