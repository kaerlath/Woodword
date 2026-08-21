export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (request.method === "GET" && url.pathname === "/") {
      return jsonResponse({ service: "Woodword Relay", status: "awake", message: "The Wood listens." });
    }

    if (request.method === "POST" && url.pathname === "/live/validate") {
      const clientError = validateClient(request, env);
      if (clientError) return clientError;

      const clientId = request.headers.get("X-Woodword-Client");
      const { success } = await env.WOODWORD_RATE_LIMITER.limit({ key: `live-validation:${clientId}` });
      if (!success) return jsonResponse({ error: "rate_limited", message: "The Wood asks that you wait before presenting another code." }, 429);

      if (!hasValidLiveCode(request, env)) {
        return jsonResponse({ error: "live_access_denied", message: "That validation code is not recognized by the Wood." }, 403);
      }

      return jsonResponse({ success: true, message: "The Wood recognizes this live-listening code." });
    }

    if (request.method === "POST" && url.pathname === "/translate") {
      try {
        const clientError = validateClient(request, env);
        if (clientError) return clientError;

        const isLiveRequest = request.headers.get("X-Woodword-Request") === "live";
        if (isLiveRequest && !hasValidLiveCode(request, env)) {
          return jsonResponse({ error: "live_access_denied", message: "Live listening is locked. Present a valid code in Woodword Settings." }, 403);
        }

        const clientId = request.headers.get("X-Woodword-Client");
        const rateKey = isLiveRequest ? `live:${clientId}` : `manual:${clientId}`;
        const { success } = await env.WOODWORD_RATE_LIMITER.limit({ key: rateKey });
        if (!success) {
          return jsonResponse({ error: "rate_limited", message: "The Wood asks that you wait a moment before speaking again." }, 429);
        }

        if (!env.GOOGLE_TRANSLATE_API_KEY) {
          return jsonResponse({ error: "relay_configuration_error", message: "The Wood cannot hear the translation service." }, 500);
        }

        let body;
        try { body = await request.json(); }
        catch { return jsonResponse({ error: "invalid_request", message: "The request could not be understood." }, 400); }

        const text = typeof body.text === "string" ? body.text.trim() : "";
        const direction = body.direction;
        if (!text) return jsonResponse({ error: "empty_text", message: "There are no words to render." }, 400);
        if (text.length > 4000) return jsonResponse({ error: "text_too_long", message: "The passage is too long for the Wood to render at once." }, 400);

        let source;
        let target;
        if (direction === "common-to-vieran") {
          source = "en";
          target = "is";
        } else if (direction === "vieran-to-common") {
          source = "is";
          target = "en";
        } else {
          return jsonResponse({ error: "invalid_direction", message: "The requested tongue is not recognized." }, 400);
        }

        const googleUrl = "https://translation.googleapis.com/language/translate/v2?key=" +
          encodeURIComponent(env.GOOGLE_TRANSLATE_API_KEY);
        const googleResponse = await fetch(googleUrl, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ q: text, source, target, format: "text" })
        });
        const googleData = await googleResponse.json();
        if (!googleResponse.ok) {
          console.error("Google Translation API error:", {
            status: googleResponse.status,
            error: googleData?.error?.message ?? "Unknown Google error"
          });
          return jsonResponse({ error: "translation_service_error", message: "The Wood could not render those words." }, 502);
        }

        const translatedText = googleData?.data?.translations?.[0]?.translatedText;
        if (!translatedText) return jsonResponse({ error: "empty_translation", message: "The Wood returned no words." }, 502);

        return jsonResponse({
          success: true,
          direction,
          sourceLanguage: direction === "common-to-vieran" ? "Common" : "Vieran",
          targetLanguage: direction === "common-to-vieran" ? "Vieran" : "Common",
          translation: decodeHtmlEntities(translatedText)
        });
      } catch (error) {
        console.error("Woodword relay error:", error);
        return jsonResponse({ error: "relay_error", message: "The Wood has fallen silent for the moment." }, 500);
      }
    }

    return jsonResponse({ error: "not_found", message: "No path through the Wood leads there." }, 404);
  }
};

function validateClient(request, env) {
  const clientToken = request.headers.get("X-Woodword-Token");
  if (!env.WOODWORD_CLIENT_TOKEN || !clientToken || clientToken !== env.WOODWORD_CLIENT_TOKEN) {
    return jsonResponse({ error: "unauthorized", message: "The Wood does not know your voice." }, 401);
  }
  const clientId = request.headers.get("X-Woodword-Client");
  if (!clientId || clientId.length < 16 || clientId.length > 128) {
    return jsonResponse({ error: "invalid_client", message: "The Wood cannot discern who speaks." }, 400);
  }
  return null;
}

function hasValidLiveCode(request, env) {
  const code = request.headers.get("X-Woodword-Live-Code");
  return Boolean(env.WOODWORD_LIVE_ACCESS_CODE && code && code === env.WOODWORD_LIVE_ACCESS_CODE);
}

function jsonResponse(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { "Content-Type": "application/json; charset=UTF-8", "Cache-Control": "no-store" }
  });
}

function decodeHtmlEntities(text) {
  return text.replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, "&")
    .replace(/&lt;/g, "<").replace(/&gt;/g, ">");
}
