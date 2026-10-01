<?php
declare(strict_types=1);
require dirname(__DIR__, 2) . '/lib/bootstrap.php';
require_method('POST');
$token = bearer_access_token();
$record = $token === null ? null : validate_access_token($token);
if ($record === null) { json_response(['error' => 'unauthorized'], 401); }
json_response(issue_beta_ticket($record));

function issue_beta_ticket(array $record): array
{
    $steamId = $record['steam_id'] ?? null;
    if (!is_string($steamId) || preg_match('/^[0-9]{17}$/D', $steamId) !== 1) {
        json_response(['error' => 'invalid_session'], 401);
    }

    $config = read_json_file(BNL_PRIVATE_ROOT . '/game-auth-config.json');
    $keyId = is_string($config['key_id'] ?? null)
        ? $config['key_id']
        : BNL_GAME_TICKET_DEFAULT_KEY_ID;
    $privateKeyPem = @file_get_contents(BNL_PRIVATE_ROOT . '/game-auth-private.pem');
    if (!is_string($privateKeyPem)) {
        json_response(['error' => 'ticket_service_unavailable'], 503);
    }

    $privateKey = openssl_pkey_get_private($privateKeyPem);
    if ($privateKey === false) {
        json_response(['error' => 'ticket_service_unavailable'], 503);
    }

    $now = time();
    $displayName = $record['display_name'] ?? default_display_name($steamId);
    if (!is_string($displayName) || trim($displayName) === '') {
        $displayName = default_display_name($steamId);
    }
    $displayName = truncate_utf8_bytes(trim($displayName), 96);
    $header = [
        'alg' => 'RS256',
        'typ' => 'JWT',
        'kid' => $keyId,
    ];
    $payload = [
        'iss' => BNL_GAME_TICKET_ISSUER,
        'aud' => 'bnl-beta65-game',
        'sub' => $steamId,
        'name' => $displayName,
        'jti' => bin2hex(random_bytes(16)),
        'iat' => $now,
        'nbf' => $now - 5,
        'exp' => $now + BNL_GAME_TICKET_TTL_SECONDS,
    ];
    $encodedHeader = base64url_encode(json_encode($header, JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR));
    $encodedPayload = base64url_encode(json_encode($payload, JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR));
    $signingInput = $encodedHeader . '.' . $encodedPayload;
    $signature = '';
    if (!openssl_sign($signingInput, $signature, $privateKey, OPENSSL_ALGO_SHA256)) {
        json_response(['error' => 'ticket_service_unavailable'], 503);
    }

    return [
        'ticket' => $signingInput . '.' . base64url_encode($signature),
        'expires_in' => BNL_GAME_TICKET_TTL_SECONDS,
        'steam_id' => $steamId,
        'display_name' => $displayName,
    ];
}

