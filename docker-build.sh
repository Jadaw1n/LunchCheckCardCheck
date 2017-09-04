#/bin/bash

docker-compose -f docker-compose.ci.build.yml up \
  && docker-compose -f docker-compose.yml -f docker-compose.override.yml build

docker-compose -f docker-compose.yml -f docker-compose.override.yml up
