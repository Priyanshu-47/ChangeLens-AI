"""Shared model helpers. The wire contract is camelCase (matches the public API)."""

from pydantic import BaseModel, ConfigDict
from pydantic.alias_generators import to_camel


class ApiModel(BaseModel):
    """Base model: camelCase JSON in/out, snake_case Python field names."""

    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)
