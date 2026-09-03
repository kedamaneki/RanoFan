"""Ollama API クライアント."""

from __future__ import annotations

import logging
import time
from typing import Optional

import requests

logger = logging.getLogger(__name__)


class OllamaError(Exception):
    """Ollama API 呼び出し失敗."""


class OllamaClient:
    def __init__(
        self,
        base_url: str = "http://localhost:11434",
        model: str = "qwen2.5:14b",
        timeout: int = 600,
        max_retries: int = 3,
        retry_delay: float = 5.0,
    ) -> None:
        self.base_url = base_url.rstrip("/")
        self.model = model
        self.timeout = timeout
        self.max_retries = max_retries
        self.retry_delay = retry_delay

    def health_check(self) -> bool:
        try:
            r = requests.get(f"{self.base_url}/api/tags", timeout=10)
            return r.status_code == 200
        except requests.RequestException:
            return False

    def generate(
        self,
        system: str,
        prompt: str,
        temperature: float = 0.8,
    ) -> str:
        payload = {
            "model": self.model,
            "messages": [
                {"role": "system", "content": system},
                {"role": "user", "content": prompt},
            ],
            "stream": False,
            "options": {"temperature": temperature},
        }
        url = f"{self.base_url}/api/chat"
        last_err: Optional[Exception] = None

        for attempt in range(1, self.max_retries + 1):
            try:
                logger.info("Ollama リクエスト (試行 %d/%d): model=%s", attempt, self.max_retries, self.model)
                resp = requests.post(url, json=payload, timeout=self.timeout)
                resp.raise_for_status()
                data = resp.json()
                content = data.get("message", {}).get("content", "")
                if not content.strip():
                    raise OllamaError("Ollama が空の応答を返しました")
                return content.strip()
            except (requests.RequestException, OllamaError, KeyError, ValueError) as exc:
                last_err = exc
                logger.warning("Ollama エラー (試行 %d): %s", attempt, exc)
                if attempt < self.max_retries:
                    time.sleep(self.retry_delay * attempt)

        raise OllamaError(f"Ollama 呼び出しが {self.max_retries} 回失敗: {last_err}") from last_err
