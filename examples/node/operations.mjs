// Each invocation performs exactly one logical operation. No retry, fallback,
// pagination, hidden tool loop or implicit BYOK/Hybrid is enabled here.
export async function runExample(client, config, operation, signal = AbortSignal.timeout(60_000)) {
  const options = { signal, headers: { 'Idempotency-Key': config.idempotencyKey } };
  if (operation === 'models') {
    const { data, response } = await client.models.list(options).withResponse();
    return { requestId: response.headers.get('x-request-id'), models: data.data };
  }
  if (operation === 'embeddings') {
    const { data, response } = await client.embeddings.create({ model: config.model,
      input: ['Hello', 'World'], encoding_format: 'float' }, options).withResponse();
    return { requestId: response.headers.get('x-request-id'), usage: data.usage,
      vectors: data.data.map(item => ({ index: item.index, dimensions: item.embedding.length })) };
  }
  if (operation === 'chat' || operation === 'chat-stream') {
    const stream = operation === 'chat-stream';
    const body = { model: config.model, messages: [{ role: 'user', content: 'Hello' }],
      max_completion_tokens: 32, stream,
      ...(stream ? { stream_options: { include_usage: true } } : {}) };
    const { data, response } = await client.chat.completions.create(body, options).withResponse();
    if (!stream) return { requestId: response.headers.get('x-request-id'), status: data.choices[0]?.finish_reason,
      text: data.choices[0]?.message.content, refusal: data.choices[0]?.message.refusal ?? null, usage: data.usage ?? null };
    let text = ''; let refusal = ''; let status = null; let usage = null; let deltas = 0;
    for await (const chunk of data) {
      const choice = chunk.choices[0];
      if (choice?.delta.content) { text += choice.delta.content; deltas++; }
      if (choice?.delta.refusal) refusal += choice.delta.refusal;
      if (choice?.finish_reason) status = choice.finish_reason;
      if (chunk.usage) usage = chunk.usage;
    }
    if (!status) throw new Error('Stream ended without a finish reason.');
    return { requestId: response.headers.get('x-request-id'), status, text, refusal, usage, deltas };
  }
  if (operation === 'responses' || operation === 'responses-stream') {
    const stream = operation === 'responses-stream';
    const { data, response } = await client.responses.create({ model: config.model,
      input: 'Hello', max_output_tokens: 32, store: false, stream }, options).withResponse();
    let terminal = stream ? null : data; let deltas = 0;
    if (stream) {
      for await (const event of data) {
        if (event.type === 'response.output_text.delta') deltas++;
        if (event.type === 'response.failed') throw new Error('Response failed after streaming began.');
        if (event.type === 'response.completed' || event.type === 'response.incomplete') terminal = event.response;
      }
    }
    if (!terminal || !['completed', 'incomplete'].includes(terminal.status)) throw new Error('Missing successful terminal response.');
    const content = terminal.output.filter(item => item.type === 'message').flatMap(item => item.content);
    return { requestId: response.headers.get('x-request-id'), status: terminal.status,
      incompleteDetails: terminal.incomplete_details ?? null,
      text: content.filter(part => part.type === 'output_text').map(part => part.text).join(''),
      refusal: content.filter(part => part.type === 'refusal').map(part => part.refusal).join(''),
      usage: terminal.usage ?? null, ...(stream ? { deltas } : {}) };
  }
  throw new Error('Unsupported example operation.');
}
