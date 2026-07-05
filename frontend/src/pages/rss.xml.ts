import type { APIContext } from "astro";
import { localeFeed } from "../blog/rss";

export const GET = (context: APIContext) => localeFeed(context, "en");
