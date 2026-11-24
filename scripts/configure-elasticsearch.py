#!/usr/bin/env python3
"""Configure local Elasticsearch for Regent Agent's native OTLP ingestion."""
import argparse
import base64
import json
import os
import urllib.error
import urllib.parse
import urllib.request


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--endpoint', default=os.environ.get('ELASTICSEARCH_ENDPOINT', 'http://127.0.0.1:9200'))
    parser.add_argument('--username', default=os.environ.get('ELASTICSEARCH_USERNAME', 'elastic'))
    parser.add_argument('--password', default=os.environ.get('ELASTICSEARCH_PASSWORD'))
    args = parser.parse_args()
    endpoint = args.endpoint.rstrip('/')
    parsed = urllib.parse.urlparse(endpoint)
    if parsed.scheme not in {'http', 'https'} or not parsed.hostname or parsed.query or parsed.fragment:
        raise SystemExit('Elasticsearch endpoint must be an HTTP(S) base URL without query or fragment.')

    headers = {'Content-Type': 'application/json'}
    if args.password:
        token = base64.b64encode(f'{args.username}:{args.password}'.encode()).decode()
        headers['Authorization'] = f'Basic {token}'

    def request(method, path, payload=None, content_type='application/json'):
        data = payload if isinstance(payload, bytes) else None if payload is None else json.dumps(payload).encode()
        request_headers = dict(headers)
        if content_type != 'application/json':
            request_headers['Content-Type'] = content_type
        request = urllib.request.Request(endpoint + path, data=data, method=method, headers=request_headers)
        with urllib.request.urlopen(request, timeout=15) as response:
            body = response.read()
            return response.status, json.loads(body) if body else None

    status, root = request('GET', '/')
    version = root.get('version', {}).get('number', 'unknown')
    status, health = request('GET', '/_cluster/health')
    print(f'Elasticsearch {version} cluster={health.get("cluster_name")} status={health.get("status")}')

    request('PUT', '/_component_template/regent-single-node', {
        'template': {'settings': {'index': {'number_of_replicas': 0}}},
        '_meta': {'managed': True, 'description': 'Regent Agent single-node local deployment'}
    })
    for name in ('logs-otel@template', 'traces-otel@template', 'metrics-otel@template'):
        encoded = urllib.parse.quote(name, safe='')
        _, response = request('GET', f'/_index_template/{encoded}')
        template = response['index_templates'][0]['index_template']
        template.pop('created_date_millis', None)
        template.pop('modified_date_millis', None)
        composed = template.setdefault('composed_of', [])
        if 'regent-single-node' not in composed:
            composed.insert(0, 'regent-single-node')
        request('PUT', f'/_index_template/{encoded}', template)
        print(f'Updated {name}: zero replicas')

    _, indices = request('GET', '/_cat/indices?format=json&h=index&expand_wildcards=open')
    for item in indices or []:
        index = item['index']
        if not index.startswith('.ds-') or '.otel-' not in index:
            continue
        request('PUT', f'/{urllib.parse.quote(index, safe="")}/_settings', {
            'index': {'number_of_replicas': 0}
        })
        print(f'Updated {index}: zero replicas')

    for signal in ('logs', 'traces', 'metrics'):
        status, _ = request('POST', f'/_otlp/v1/{signal}', b'', content_type='application/x-protobuf')
        if status != 200:
            raise SystemExit(f'Native OTLP {signal} endpoint returned HTTP {status}.')
        print(f'Native OTLP {signal} endpoint: HTTP {status}')

    _, health = request('GET', '/_cluster/health')
    print(f'PASS: Elasticsearch native OTLP configured; cluster status={health.get("status")}')


if __name__ == '__main__':
    try:
        main()
    except urllib.error.HTTPError as error:
        raise SystemExit(f'Elasticsearch API failed: HTTP {error.code}.') from None
    except urllib.error.URLError:
        raise SystemExit('Elasticsearch is unreachable.') from None
