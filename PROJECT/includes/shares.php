<?php

class ShareLinkService
{
    private string $path;
    private array  $links;

    public function __construct(string $dataDir)
    {
        if (!is_dir($dataDir)) {
            mkdir($dataDir, 0755, true);
        }
        $this->path  = $dataDir . '/shares.json';
        $this->links = $this->load();
    }

    private function load(): array
    {
        if (!file_exists($this->path)) {
            return [];
        }
        return json_decode(file_get_contents($this->path), true) ?? [];
    }

    private function save(): void
    {
        file_put_contents(
            $this->path,
            json_encode($this->links, JSON_PRETTY_PRINT | JSON_UNESCAPED_UNICODE),
            LOCK_EX
        );
    }

    public function create(string $fileName, int $maxDownloads): string
    {
        $token = bin2hex(random_bytes(32));
        $this->links[] = [
            'token'     => $token,
            'fileName'  => $fileName,
            'remaining' => $maxDownloads,
            'createdAt' => date('c'),
        ];
        $this->save();
        return $token;
    }

    public function consume(string $token): ?string
    {
        foreach ($this->links as $i => &$link) {
            if (hash_equals($link['token'], $token)) {
                if ($link['remaining'] <= 0) {
                    return null;
                }
                $link['remaining']--;
                $fileName = $link['fileName'];
                if ($link['remaining'] === 0) {
                    array_splice($this->links, $i, 1);
                }
                $this->save();
                return $fileName;
            }
        }
        return null;
    }
}
