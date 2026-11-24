FROM nginx:stable-alpine
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY index.html app.js styles.css /usr/share/nginx/html/
EXPOSE 80
