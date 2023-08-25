import { html, Component, render } from 'https://unpkg.com/htm/preact/standalone.module.js'

const LoadingWidget = () => html`
<svg class="pl" viewBox="0 0 128 128" width="128px" height="128px" xmlns="http://www.w3.org/2000/svg">
	<defs>
		<linearGradient id="pl-grad" x1="0" y1="0" x2="0" y2="1">
			<stop offset="0%" stop-color="hsl(193,90%,55%)" />
			<stop offset="100%" stop-color="hsl(223,90%,55%)" />
		</linearGradient>
	</defs>
	<circle class="pl__ring" r="56" cx="64" cy="64" fill="none" stroke="hsla(0,10%,10%,0.1)" stroke-width="16" stroke-linecap="round" />
	<path class="pl__worm" d="M92,15.492S78.194,4.967,66.743,16.887c-17.231,17.938-28.26,96.974-28.26,96.974L119.85,59.892l-99-31.588,57.528,89.832L97.8,19.349,13.636,88.51l89.012,16.015S81.908,38.332,66.1,22.337C50.114,6.156,36,15.492,36,15.492a56,56,0,1,0,56,0Z" fill="none" stroke="url(#pl-grad)" stroke-width="16" stroke-linecap="round" stroke-linejoin="round" stroke-dasharray="44 1111" stroke-dashoffset="10" />
</svg>
`;

class App extends Component {

    constructor() {
        super();
        this.state = {
            pingCount: 0,
            name: window.myData.containerName,
            navigateUrl: window.myData.navigateUrl,
            error: "",
        };
    }

    componentDidMount() {
        setTimeout(this.refresh, 1000);
    }

    refresh = async () => {

        const { name, navigateUrl, pingCount } = this.state;
        if (pingCount > 4) {
            window.location.href = navigateUrl;
            return;
        }

        this.setState({ pingCount: pingCount + 1 });
        setTimeout(this.refresh, 1000);


        //const { name, navigateUrl, pingCount } = this.state;
        //const response = await fetch("/ping/" + name);

        //if (response.status == 404) {
        //    this.setState({ error: "Container '" + name + "' could not be found (404)." });
        //}
        //else if (response.status == 500 && pingCount >= 10) {
        //    this.setState({ error: "Starting '" + name + "' has timed out." });
        //}
        //else if (response.status == 200) {
        //    window.location.href = navigateUrl;
        //}
        //else {
        //    this.setState({ pingCount: pingCount + 1 });
        //    setTimeout(this.refresh, 1000);
        //}


        //const containers = await response.json();
        //this.setState({
        //    loading: false,
        //    containers,
        //});
    }

    render({ }, { pingCount, name, error }) {

        return html`
<div class="center">
    <div class="network-label">Waiting for ${name} (${pingCount})...</div>
    <br />
    <${LoadingWidget} />
</div>
`;
    }
}

render(html`<${App} />`, document.body);