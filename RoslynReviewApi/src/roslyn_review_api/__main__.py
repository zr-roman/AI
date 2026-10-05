"""``python -m roslyn_review_api``: serve the API with uvicorn, configured from ROSLYN_REVIEW_* variables."""

from __future__ import annotations

import argparse
import logging
import sys

import uvicorn

from .app import create_app
from .settings import Settings, SettingsError


def main() -> None:
    parser = argparse.ArgumentParser(prog="roslyn-review-api", description=__doc__)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8000)
    options = parser.parse_args()

    try:
        settings = Settings.from_env()
    except SettingsError as ex:
        sys.exit(str(ex))

    logging.basicConfig(level=logging.INFO, format="%(levelname)s %(name)s: %(message)s")
    uvicorn.run(create_app(settings), host=options.host, port=options.port)


if __name__ == "__main__":
    main()
